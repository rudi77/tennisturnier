using Matchday.Server.Api;
using Matchday.Server.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Matchday.Server.Tests;

/// <summary>Einmal am Tag eine Kopie, und nach einer Woche fällt die älteste weg.</summary>
public sealed class SicherungTests : IDisposable
{
    private readonly Aufbau _a = new();
    private readonly string _ordner = Path.Combine(Path.GetTempPath(), $"matchday-sicherung-{Guid.NewGuid():N}");
    private readonly Uhr _uhr = new(new DateTimeOffset(2026, 9, 26, 3, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        _a.Dispose();

        if (Directory.Exists(_ordner))
        {
            Directory.Delete(_ordner, recursive: true);
        }
    }

    [Fact]
    public async Task Eine_Kopie_am_Tag_und_sie_traegt_die_Turniere()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"));
        var sicherung = Sicherung(keep: 7);

        await sicherung.RunOnceAsync();
        _uhr.Jetzt += TimeSpan.FromHours(5);
        await sicherung.RunOnceAsync();

        // Fünf Stunden später gibt es noch keine zweite.
        var kopie = Assert.Single(Kopien());
        Assert.Equal("matchday-20260926-0300.db", Path.GetFileName(kopie));

        // Und die Kopie ist eine Datenbank mit dem Turnier darin.
        var gelesen = await new TournamentStore($"Data Source={kopie};Pooling=False").FindAsync(t.Id);
        Assert.Equal("Sommercup", gelesen!.Name);

        _uhr.Jetzt += TimeSpan.FromDays(1);
        await sicherung.RunOnceAsync();
        Assert.Equal(2, Kopien().Length);
    }

    [Fact]
    public async Task Nach_Keep_Tagen_faellt_die_aelteste_weg_und_Fremdes_bleibt()
    {
        Directory.CreateDirectory(_ordner);
        File.WriteAllText(Path.Combine(_ordner, "matchday-kaputt.db"), "keine Kopie");
        File.WriteAllText(Path.Combine(_ordner, "notizen.txt"), "auch keine");
        var sicherung = Sicherung(keep: 2);

        for (var tag = 0; tag < 4; tag++)
        {
            await sicherung.RunOnceAsync();
            _uhr.Jetzt += TimeSpan.FromDays(1);
        }

        Assert.Equal(["matchday-20260928-0300.db", "matchday-20260929-0300.db"], Kopien().Select(k => Path.GetFileName(k)!).Order().ToArray());
        Assert.True(File.Exists(Path.Combine(_ordner, "matchday-kaputt.db")));
        Assert.True(File.Exists(Path.Combine(_ordner, "notizen.txt")));
    }

    [Fact]
    public async Task Mindestens_eine_Kopie_bleibt()
    {
        var sicherung = Sicherung(keep: 0);

        await sicherung.RunOnceAsync();
        _uhr.Jetzt += TimeSpan.FromDays(1);
        await sicherung.RunOnceAsync();

        Assert.Single(Kopien());
    }

    [Fact]
    public async Task Scheitert_die_Sicherung_laeuft_die_Anwendung_weiter()
    {
        // Wo ein Ordner hin soll, liegt eine Datei.
        File.WriteAllText(_ordner, "im Weg");

        try
        {
            await Sicherung(keep: 7).RunOnceAsync();
        }
        finally
        {
            File.Delete(_ordner);
        }
    }

    [Fact]
    public async Task Im_Dienst_laeuft_sie_von_selbst_und_haelt_beim_Herunterfahren_an()
    {
        using var abbruch = new CancellationTokenSource();
        var sicherung = new Backup(_a.Store, new BackupOptions { Path = _ordner }, TimeProvider.System, NullLogger<Backup>.Instance)
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };

        await sicherung.StartAsync(abbruch.Token);
        await WarteAuf(() => Kopien().Length == 1);
        await sicherung.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Mit_Backup_Path_haengt_die_Anwendung_den_Dienst_selbst_ein()
    {
        var db = Path.Combine(Path.GetTempPath(), $"matchday-dienst-{Guid.NewGuid():N}.db");

        using (var fabrik = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={db};Pooling=False");
            builder.UseSetting("Backup:Path", _ordner);
        }))
        {
            fabrik.CreateClient();
            await WarteAuf(() => Kopien().Length == 1);
        }

        Aufbau.Wegräumen(db);
    }

    private Backup Sicherung(int keep) =>
        new(_a.Store, new BackupOptions { Path = _ordner, Keep = keep }, _uhr, NullLogger<Backup>.Instance);

    private string[] Kopien() => Directory.Exists(_ordner) ? Directory.GetFiles(_ordner, "matchday-2*.db") : [];

    private static async Task WarteAuf(Func<bool> bedingung)
    {
        for (var i = 0; i < 200 && !bedingung(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(bedingung());
    }

    /// <summary>Eine Uhr, die der Test stellt.</summary>
    private sealed class Uhr(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Jetzt { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Jetzt;
    }
}
