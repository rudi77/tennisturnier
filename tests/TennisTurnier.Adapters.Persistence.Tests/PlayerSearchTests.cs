using TennisTurnier.Adapters.Persistence.Sqlite.Repositories;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Tests;

/// <summary>
/// Die Spielersuche gegen echtes SQLite.
///
/// Die Platzhalter von LIKE müssen über die ESCAPE-Klausel entschärft werden.
/// Die Zeichenklassen-Schreibweise <c>[%]</c> ist eine Eigenheit von SQL Server
/// und bedeutet in SQLite und PostgreSQL etwas anderes — genau die Art
/// anbieterspezifischer Annahme, vor der ADR-0006 warnt.
/// </summary>
public sealed class PlayerSearchTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();

    public async Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        await using var db = _database.NewContext();

        db.Players.Add(new Player(Guid.NewGuid(), "Anna", "Müller"));
        db.Players.Add(new Player(Guid.NewGuid(), "Eva", "Berger"));
        db.Players.Add(new Player(Guid.NewGuid(), "Lisa", "Mayr-Huber"));
        db.Players.Add(new Player(Guid.NewGuid(), "Sarah", "100%Fokus"));
        db.Players.Add(new Player(Guid.NewGuid(), "Nina", "Wagner_Berg"));

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private async Task<IReadOnlyList<string>> SearchAsync(string term)
    {
        await using var db = _database.NewContext();
        var players = await new PlayerRepository(db).SearchAsync(term, limit: 50);

        return players.Select(p => p.LastName).ToList();
    }

    [Fact]
    public async Task Ein_Teil_des_Nachnamens_genuegt()
    {
        Assert.Equal(["Müller"], await SearchAsync("üll"));
    }

    [Fact]
    public async Task Auch_der_Vorname_wird_durchsucht()
    {
        Assert.Equal(["Berger"], await SearchAsync("Eva"));
    }

    [Fact]
    public async Task Ein_Prozentzeichen_wird_woertlich_gesucht_und_nicht_als_Platzhalter()
    {
        // Ohne korrektes Escaping lieferte die Suche nach „%" alle Spieler.
        Assert.Equal(["100%Fokus"], await SearchAsync("%"));
    }

    [Fact]
    public async Task Ein_Unterstrich_wird_woertlich_gesucht()
    {
        // Ohne Escaping stünde „_" für ein beliebiges Zeichen und träfe jeden.
        Assert.Equal(["Wagner_Berg"], await SearchAsync("_"));
    }

    [Fact]
    public async Task Eine_eckige_Klammer_ist_in_SQLite_kein_Sonderzeichen()
    {
        // Die frühere Fassung übersetzte „%" in „[%]" — in SQLite die
        // buchstäbliche Zeichenfolge, weshalb eine Suche danach nichts fand.
        Assert.Empty(await SearchAsync("["));
    }

    [Fact]
    public async Task Ein_unbekannter_Name_liefert_nichts()
    {
        Assert.Empty(await SearchAsync("Zzzzz"));
    }

    [Fact]
    public async Task Die_Trefferzahl_ist_begrenzt()
    {
        await using var db = _database.NewContext();

        var players = await new PlayerRepository(db).SearchAsync("e", limit: 2);

        Assert.Equal(2, players.Count);
    }
}
