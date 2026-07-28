using Microsoft.EntityFrameworkCore;
using TennisTurnier.Adapters.Persistence.Sqlite;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Tests;

/// <summary>
/// Eine echte SQLite-Datei je Test.
///
/// Bewusst nicht der In-Memory-Provider von EF: der verhält sich bei Typisierung,
/// Fremdschlüsseln und Transaktionen anders als SQLite und würde genau die Fehler
/// durchlassen, wegen derer diese Tests existieren.
/// </summary>
public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _path;

    public SqliteTestDatabase()
    {
        _path = Path.Combine(Path.GetTempPath(), $"tennisturnier-test-{Guid.NewGuid():N}.db");

        using var db = NewContext();
        db.Database.Migrate();
    }

    /// <summary>Der Benutzer, als der die nächsten Abfragen laufen.</summary>
    public UserPrincipal ActingAs { get; set; } = UserPrincipal.System;

    /// <summary>
    /// Jeder Kontext bekommt seine <em>eigene</em> <see cref="IUserContext"/>-Instanz,
    /// genau wie jeder Request in der Anwendung.
    ///
    /// Das ist kein Detail: das Filter-Lambda in <c>OnModelCreating</c> schließt
    /// über denjenigen DbContext ab, der das Modell gebaut hat. Teilten sich die
    /// Kontexte eine Instanz, liefe der Test daran vorbei und prüfte nur, dass
    /// eine veränderliche Eigenschaft gelesen wird.
    /// </summary>
    public TennisTurnierDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TennisTurnierDbContext>()
            .UseSqlite($"Data Source={_path}")
            .Options;

        return new TennisTurnierDbContext(options, new FixedUserContext(ActingAs));
    }

    public ValueTask DisposeAsync()
    {
        // Die Verbindungen des Pools halten die Datei offen; ohne das Leeren
        // bleibt sie auf Windows gesperrt und der Aufräumversuch scheitert.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var file in new[] { _path, $"{_path}-shm", $"{_path}-wal" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        return ValueTask.CompletedTask;
    }

    private sealed class FixedUserContext : IUserContext
    {
        public FixedUserContext(UserPrincipal current) => Current = current;

        public UserPrincipal Current { get; }
    }
}
