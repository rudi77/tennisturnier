using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    public TennisTurnierDbContext NewContext(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<TennisTurnierDbContext>()
            .UseSqlite($"Data Source={_path}");

        // Für den einen Test, der einen Schreibkonflikt zuverlässig herstellen
        // muss: ein Interceptor schiebt die konkurrierende Zeile genau zwischen
        // Prüfung und Speichern ein. Zwei Aufrufe nacheinander träfen einander
        // nie, und zwei nebenläufige nur manchmal.
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new TennisTurnierDbContext(builder.Options, new FixedUserContext(ActingAs));
    }

    /// <summary>
    /// Hält die Datei exklusiv, bis das Ergebnis entsorgt wird.
    ///
    /// SQLite lässt nur einen Schreiber zu; wer währenddessen schreiben will,
    /// bekommt SQLITE_BUSY. Genau das ist am Turniertag der häufigste Konflikt,
    /// und genau dafür gibt es die Übersetzung in der Arbeitseinheit.
    /// </summary>
    public Sperre Sperren()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}");
        connection.Open();

        using var befehl = connection.CreateCommand();
        befehl.CommandText = "BEGIN EXCLUSIVE;";
        befehl.ExecuteNonQuery();

        return new Sperre(connection);
    }

    /// <summary>Eine offene exklusive Transaktion auf einer eigenen Verbindung.</summary>
    public sealed class Sperre : IAsyncDisposable
    {
        private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

        internal Sperre(Microsoft.Data.Sqlite.SqliteConnection connection) => _connection = connection;

        public async ValueTask DisposeAsync()
        {
            await using var befehl = _connection.CreateCommand();
            befehl.CommandText = "ROLLBACK;";
            await befehl.ExecuteNonQueryAsync();

            await _connection.DisposeAsync();
        }
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
