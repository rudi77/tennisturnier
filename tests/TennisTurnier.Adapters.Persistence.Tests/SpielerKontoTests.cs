using Microsoft.EntityFrameworkCore;
using TennisTurnier.Adapters.Persistence.Sqlite.Repositories;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Tests;

/// <summary>
/// Die Verbindung von Spieler und Konto gegen echtes SQLite.
///
/// Zwei Zusagen, die nur die Datenbank halten kann: ein Konto gehört zu genau
/// einem Spieler, und beliebig viele Spieler gehören zu keinem. Die zweite ist
/// der Grund, warum der eindeutige Index hier nicht genügt hätte, wenn NULL
/// wie ein Wert behandelt würde.
/// </summary>
public sealed class SpielerKontoTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();

    public Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Der_Spieler_zu_einem_Konto_laesst_sich_finden()
    {
        var konto = Guid.NewGuid();

        await using (var db = _database.NewContext())
        {
            var spieler = new Player(Guid.NewGuid(), "Anna", "Müller");
            spieler.LinkAccount(konto);
            db.Players.Add(spieler);
            db.Players.Add(new Player(Guid.NewGuid(), "Bea", "Berger"));
            await db.SaveChangesAsync();
        }

        await using var lesen = _database.NewContext();
        var gefunden = await new PlayerRepository(lesen).FindByUserAccountAsync(konto);

        Assert.NotNull(gefunden);
        Assert.Equal("Müller, Anna", gefunden.DisplayName);
    }

    [Fact]
    public async Task Ohne_Konto_findet_sich_niemand()
    {
        await using var db = _database.NewContext();

        Assert.Null(await new PlayerRepository(db).FindByUserAccountAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Viele_Spieler_ohne_Konto_stehen_nebeneinander()
    {
        // Der Normalfall einer hochgeladenen Teilnehmerliste. Zählte NULL als
        // Wert, ließe der eindeutige Index genau einen davon zu — und der
        // Import scheiterte am zweiten Namen.
        await using var db = _database.NewContext();

        db.Players.Add(new Player(Guid.NewGuid(), "Anna", "Müller"));
        db.Players.Add(new Player(Guid.NewGuid(), "Bea", "Berger"));
        db.Players.Add(new Player(Guid.NewGuid(), "Cora", "Huber"));

        await db.SaveChangesAsync();

        Assert.Equal(3, await db.Players.CountAsync());
    }

    [Fact]
    public async Task Dasselbe_Konto_an_zwei_Spielern_weist_die_Datenbank_ab()
    {
        // Die Domäne verhindert es am einzelnen Spieler; sie kann aber nicht
        // sehen, was am anderen steht. Den Ausschlag gibt der Index.
        var konto = Guid.NewGuid();

        await using (var db = _database.NewContext())
        {
            var erster = new Player(Guid.NewGuid(), "Anna", "Müller");
            erster.LinkAccount(konto);
            db.Players.Add(erster);
            await db.SaveChangesAsync();
        }

        await using var zweiter = _database.NewContext();
        var doppelt = new Player(Guid.NewGuid(), "Anna", "Müller");
        doppelt.LinkAccount(konto);
        zweiter.Players.Add(doppelt);

        await Assert.ThrowsAsync<DbUpdateException>(() => zweiter.SaveChangesAsync());
    }
}
