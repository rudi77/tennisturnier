using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TennisTurnier.Adapters.Persistence.Sqlite;

public static class DatabaseMigrator
{
    /// <summary>
    /// Bringt das Schema auf den Stand der Migrationen. Bewusst ein eigener,
    /// einmal aufzurufender Schritt und kein Nebeneffekt des Anwendungsstarts:
    /// zwei gleichzeitig startende Prozesse würden einander sonst überholen.
    /// </summary>
    public static async Task MigrateDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TennisTurnierDbContext>();

        await db.Database.MigrateAsync(cancellationToken);

        await AktiviereWalAsync(db, cancellationToken);
    }

    /// <summary>
    /// Schaltet die Datei auf das Write-Ahead-Log um.
    ///
    /// Im voreingestellten Rollback-Journal sperren Leser den Commit und der
    /// Commit die Leser. Das ist genau die Kombination, die hier auftritt: die
    /// öffentliche Ansicht wird von „einigen hundert Zuschauern" abgefragt
    /// (ADR-0003), während am Platz Ergebnisse eingetragen werden. Mit WAL
    /// lesen sie weiter, während geschrieben wird.
    ///
    /// Einmal beim Wandern und nicht bei jeder Verbindung: die Einstellung
    /// steht in der Datei und überlebt den Neustart. Für eine
    /// Speicherdatenbank ohne Datei gibt es sie nicht — SQLite antwortet dann
    /// mit dem Modus, den es behält, und das ist kein Fehler, sondern die
    /// Auskunft, dass hier nichts umzustellen war.
    /// </summary>
    private static Task AktiviereWalAsync(
        TennisTurnierDbContext db,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
}
