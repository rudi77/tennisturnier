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
    private readonly MutableUserContext _userContext = new();

    public SqliteTestDatabase()
    {
        _path = Path.Combine(Path.GetTempPath(), $"tennisturnier-test-{Guid.NewGuid():N}.db");

        using var db = NewContext();
        db.Database.Migrate();
    }

    /// <summary>Der Benutzer, als der die nächsten Abfragen laufen.</summary>
    public UserPrincipal ActingAs
    {
        get => _userContext.Current;
        set => _userContext.Current = value;
    }

    public IUserContext UserContext => _userContext;

    public TennisTurnierDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TennisTurnierDbContext>()
            .UseSqlite($"Data Source={_path}")
            .Options;

        return new TennisTurnierDbContext(options, _userContext);
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

    private sealed class MutableUserContext : IUserContext
    {
        public UserPrincipal Current { get; set; } = UserPrincipal.System;
    }
}
