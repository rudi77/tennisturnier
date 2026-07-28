using TennisTurnier.Adapters.Persistence.Sqlite.Repositories;
using TennisTurnier.Domain.PublicView;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Tests;

/// <summary>
/// Die Projektion ist die einzige Tabelle ohne Query-Filter (ADR-0003) — und das
/// muss geprüft sein. Läge auf ihr versehentlich einer, bekäme jeder Zuschauer
/// ein 404, und der Fehler fiele erst auf, wenn jemand die Ansicht ohne
/// Anmeldung öffnet.
/// </summary>
public sealed class TournamentProjectionStoreTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();

    private Guid _tournamentId;

    public async Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        await using var db = _database.NewContext();

        _tournamentId = Guid.NewGuid();
        db.TournamentProjections.Add(
            new TournamentProjection(_tournamentId, "{\"name\":\"Clubmeisterschaft\"}", DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Ein_nicht_angemeldeter_Aufrufer_liest_die_Projektion()
    {
        _database.ActingAs = UserPrincipal.Anonymous;
        await using var db = _database.NewContext();

        var projection = await new TournamentProjectionStore(db).FindAsync(_tournamentId);

        Assert.NotNull(projection);
        Assert.Contains("Clubmeisterschaft", projection.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_geaenderter_Stand_uebersteht_den_Neuaufbau()
    {
        var updated = new DateTimeOffset(2026, 5, 16, 11, 30, 0, TimeSpan.Zero);

        await using (var db = _database.NewContext())
        {
            var store = new TournamentProjectionStore(db);
            var projection = await store.FindAsync(_tournamentId);

            Assert.True(projection!.Replace("{\"name\":\"Clubmeisterschaft\",\"phases\":[]}", updated));
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            var projection = await new TournamentProjectionStore(db).FindAsync(_tournamentId);

            Assert.Equal(2, projection!.Version);
            Assert.Equal(updated, projection.UpdatedAt);
            Assert.Contains("phases", projection.Json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Zwei_gleichzeitige_Neuaufbauten_werden_erkannt()
    {
        // Zwei Schiedsrichter tragen gleichzeitig ein Ergebnis ein; beide lösen
        // einen Neuaufbau aus. Ohne Token schriebe der langsamere seinen älteren
        // Stand über den neueren.
        await using var first = _database.NewContext();
        await using var second = _database.NewContext();

        var a = await new TournamentProjectionStore(first).FindAsync(_tournamentId);
        var b = await new TournamentProjectionStore(second).FindAsync(_tournamentId);

        a!.Replace("{\"stand\":\"a\"}", DateTimeOffset.UtcNow);
        await first.SaveChangesAsync();

        b!.Replace("{\"stand\":\"b\"}", DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>(
            () => second.SaveChangesAsync());
    }
}
