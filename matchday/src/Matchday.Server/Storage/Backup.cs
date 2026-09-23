using System.Globalization;

namespace Matchday.Server.Storage;

/// <summary>Wohin und wie viele: <c>Backup__Path</c> und <c>Backup__Keep</c>.</summary>
public sealed class BackupOptions
{
    /// <summary>Der Ordner für die Kopien. Leer heißt: keine Sicherung — so in der Entwicklung.</summary>
    public string? Path { get; set; }

    /// <summary>Wie viele Tage zurück es Kopien gibt.</summary>
    public int Keep { get; set; } = 7;
}

/// <summary>
/// Einmal am Tag eine Kopie der Datenbank, und die ältesten fallen weg. Die
/// Kopien liegen auf demselben Datenträger — gegen einen verlorenen Datenträger
/// hilft das nicht, gegen ein versehentlich gelöschtes Turnier oder eine
/// kaputte Datei schon. Scheitert eine Sicherung, läuft die Anwendung weiter;
/// es steht im Protokoll.
/// </summary>
public sealed class Backup(TournamentStore store, BackupOptions options, TimeProvider clock, ILogger<Backup> log) : BackgroundService
{
    private const string Prefix = "matchday-";

    /// <summary>Wie oft nachgesehen wird, ob eine neue Kopie fällig ist. Die Tests drehen es herunter.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Beim Herunterfahren endet das Warten still, und die Schleife mit ihm.
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await Task.Delay(Interval, clock, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>Eine Kopie, wenn die letzte älter als einen Tag ist — und die überzähligen weg.</summary>
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        try
        {
            var ordner = Directory.CreateDirectory(options.Path!);
            // Nur, was dem eigenen Muster folgt: Eine fremde Datei im Ordner darf
            // weder als Kopie zählen noch gelöscht werden.
            var kopien = ordner.GetFiles($"{Prefix}*.db")
                .Select(f => (Datei: f, Stand: Stand(f)))
                .Where(k => k.Stand is not null)
                .Select(k => (k.Datei, Stand: k.Stand!.Value))
                .OrderByDescending(k => k.Stand)
                .ToList();
            var jetzt = clock.GetUtcNow();

            if (kopien.Count == 0 || jetzt - kopien[0].Stand >= TimeSpan.FromDays(1))
            {
                var datei = System.IO.Path.Combine(ordner.FullName, $"{Prefix}{jetzt:yyyyMMdd-HHmm}.db");
                await store.BackupAsync(datei, ct);
                log.LogInformation("Datenbank gesichert nach {Datei}", datei);
                kopien.Insert(0, (new FileInfo(datei), jetzt));
            }

            foreach (var alt in kopien.Skip(Math.Max(1, options.Keep)))
            {
                alt.Datei.Delete();
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Die Sicherung der Datenbank ist gescheitert.");
        }
    }

    /// <summary>Wann eine Kopie entstand — aus ihrem Namen, nicht aus dem Dateisystem, das beim Kopieren lügt.</summary>
    private static DateTimeOffset? Stand(FileInfo kopie) =>
        DateTimeOffset.TryParseExact(
            kopie.Name[Prefix.Length..^3],
            "yyyyMMdd-HHmm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var stand)
            ? stand
            : null;
}
