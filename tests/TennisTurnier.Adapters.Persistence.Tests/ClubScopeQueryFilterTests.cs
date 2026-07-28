using Microsoft.EntityFrameworkCore;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Tests;

/// <summary>
/// Der Query-Filter ist nach ADR-0004 die eigentliche Sicherheitsgrenze — nicht
/// die Prüfung im Endpunkt. Diese Tests sind daher keine Nebensache: sie prüfen,
/// dass ein vergessener Autorisierungsaufruf keine fremden Vereinsdaten
/// ausliefern kann.
/// </summary>
public sealed class ClubScopeQueryFilterTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();

    private Guid _clubA;
    private Guid _clubB;
    private readonly Guid _adminOfA = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var db = _database.NewContext();

        var a = new Club(Guid.NewGuid(), "TC Alpha", "Europe/Vienna");
        a.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);
        var b = new Club(Guid.NewGuid(), "TC Beta", "Europe/Vienna");
        b.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Hard, CourtLocation.Indoor);

        db.Clubs.AddRange(a, b);
        await db.SaveChangesAsync();

        _clubA = a.Id;
        _clubB = b.Id;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private UserPrincipal AdminOf(Guid clubId) =>
        new(_adminOfA, [new RoleAssignment(Guid.NewGuid(), _adminOfA, Role.ClubAdmin, ResourceScope.Club(clubId))]);

    [Fact]
    public async Task Ein_ClubAdmin_sieht_nur_seinen_eigenen_Verein()
    {
        _database.ActingAs = AdminOf(_clubA);
        await using var db = _database.NewContext();

        var clubs = await db.Clubs.ToListAsync();

        Assert.Equal([_clubA], clubs.Select(c => c.Id));
    }

    [Fact]
    public async Task Der_gezielte_Zugriff_auf_einen_fremden_Verein_liefert_nichts()
    {
        // Entscheidend: nicht „Zugriff verweigert", sondern „existiert nicht".
        // Ein 403 würde die Existenz des Vereins verraten.
        _database.ActingAs = AdminOf(_clubA);
        await using var db = _database.NewContext();

        var foreign = await db.Clubs.FirstOrDefaultAsync(c => c.Id == _clubB);

        Assert.Null(foreign);
    }

    [Fact]
    public async Task Auch_die_Plaetze_eines_fremden_Vereins_bleiben_verborgen()
    {
        // Der Filter muss an jeder club-gebundenen Entität hängen. Nur am Verein
        // wäre er wirkungslos, sobald jemand direkt über die Plätze abfragt.
        _database.ActingAs = AdminOf(_clubA);
        await using var db = _database.NewContext();

        var courts = await db.Courts.ToListAsync();

        Assert.All(courts, court => Assert.Equal(_clubA, court.ClubId));
        Assert.Single(courts);
    }

    [Fact]
    public async Task Ein_SystemAdmin_sieht_alle_Vereine()
    {
        var admin = Guid.NewGuid();
        _database.ActingAs = new UserPrincipal(
            admin,
            [new RoleAssignment(Guid.NewGuid(), admin, Role.SystemAdmin, ResourceScope.Global)]);

        await using var db = _database.NewContext();

        Assert.Equal(2, await db.Clubs.CountAsync());
    }

    [Fact]
    public async Task Ein_nicht_angemeldeter_Aufrufer_sieht_keinen_Verein()
    {
        _database.ActingAs = UserPrincipal.Anonymous;
        await using var db = _database.NewContext();

        Assert.Empty(await db.Clubs.ToListAsync());
    }

    [Fact]
    public async Task Der_Systemkontext_sieht_alles()
    {
        _database.ActingAs = UserPrincipal.System;
        await using var db = _database.NewContext();

        Assert.Equal(2, await db.Clubs.CountAsync());
    }

    [Fact]
    public async Task Der_Filter_wird_je_Benutzer_neu_ausgewertet()
    {
        // EF Core legt kompilierte Abfragepläne im Cache ab. Würde der Filter die
        // Club-Id als Konstante statt als Parameter einbauen, bekäme der zweite
        // Benutzer die Ergebnisse des ersten — ein Datenleck, das erst unter Last
        // auffiele.
        _database.ActingAs = AdminOf(_clubA);
        await using (var first = _database.NewContext())
        {
            Assert.Equal([_clubA], (await first.Clubs.ToListAsync()).Select(c => c.Id));
        }

        _database.ActingAs = AdminOf(_clubB);
        await using (var second = _database.NewContext())
        {
            Assert.Equal([_clubB], (await second.Clubs.ToListAsync()).Select(c => c.Id));
        }
    }
}
