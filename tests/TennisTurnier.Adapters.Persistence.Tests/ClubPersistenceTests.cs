using Microsoft.EntityFrameworkCore;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Tests;

public sealed class ClubPersistenceTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();

    public Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private async Task<Guid> SeedClubAsync()
    {
        await using var db = _database.NewContext();
        var club = new Club(Guid.NewGuid(), "TC Musterstadt", "Europe/Vienna", "Musterstadt");
        db.Clubs.Add(club);
        await db.SaveChangesAsync();
        return club.Id;
    }

    [Fact]
    public async Task Ein_Verein_wird_vollstaendig_gespeichert_und_geladen()
    {
        var clubId = await SeedClubAsync();

        await using var db = _database.NewContext();
        var club = await db.Clubs.SingleAsync(c => c.Id == clubId);

        Assert.Equal("TC Musterstadt", club.Name);
        Assert.Equal("Musterstadt", club.City);
        Assert.Equal("Europe/Vienna", club.TimeZoneId);
    }

    [Fact]
    public async Task Ein_neuer_Platz_an_einem_geladenen_Verein_wird_eingefuegt()
    {
        // Regression: EF Core hält einen bereits gesetzten Guid-Schlüssel für ein
        // Zeichen dafür, dass die Entität schon existiert, und schreibt ein UPDATE
        // ohne Treffer. Der Platz wäre dann stillschweigend nicht angelegt.
        var clubId = await SeedClubAsync();

        await using (var db = _database.NewContext())
        {
            var club = await db.Clubs.SingleAsync(c => c.Id == clubId);
            club.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            var club = await db.Clubs.SingleAsync(c => c.Id == clubId);
            Assert.Equal("Platz 1", Assert.Single(club.Courts).Name);
        }
    }

    [Fact]
    public async Task Oeffnungszeiten_und_Sperren_ueberleben_den_Neuaufbau()
    {
        var clubId = await SeedClubAsync();
        var period = new TimeSlot(
            new DateTimeOffset(2026, 5, 16, 14, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 5, 16, 18, 0, 0, TimeSpan.FromHours(2)));

        await using (var db = _database.NewContext())
        {
            var club = await db.Clubs.SingleAsync(c => c.Id == clubId);
            var court = club.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);
            court.AddAvailability(
                Guid.NewGuid(), DayOfWeek.Saturday,
                new TimeOnly(8, 0), new TimeOnly(22, 0),
                new DateOnly(2026, 1, 1));
            court.AddBlock(Guid.NewGuid(), period, BlockReason.LeagueMatch, "Herren 45");
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            var court = Assert.Single((await db.Clubs.SingleAsync(c => c.Id == clubId)).Courts);

            var window = Assert.Single(court.Availability);
            Assert.Equal(DayOfWeek.Saturday, window.DayOfWeek);
            Assert.Equal(new TimeOnly(8, 0), window.OpensAt);
            Assert.Null(window.ValidUntil);

            var block = Assert.Single(court.Blocks);
            Assert.Equal(BlockReason.LeagueMatch, block.Reason);
            Assert.Equal("Herren 45", block.Note);

            // Gespeichert wird in UTC (ADR-0006) — derselbe Zeitpunkt, andere Darstellung.
            Assert.Equal(period.Start, block.Period.Start);
            Assert.Equal(period.End, block.Period.End);
        }
    }

    [Fact]
    public async Task Zeitpunkte_werden_normalisiert_gespeichert_und_sortieren_korrekt()
    {
        // Der Kern von ADR-0006: SQLite sortiert TEXT lexikografisch. Ohne
        // Normalisierung auf UTC läge „10:00+02:00" hinter „09:00+00:00",
        // obwohl es derselbe Zeitpunkt ist.
        var clubId = await SeedClubAsync();

        var wienerMittag = new DateTimeOffset(2026, 5, 16, 14, 0, 0, TimeSpan.FromHours(2)); // 12:00 UTC
        var londonMorgen = new DateTimeOffset(2026, 5, 16, 13, 0, 0, TimeSpan.FromHours(1)); // 12:00 UTC
        var frueher = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero);

        await using (var db = _database.NewContext())
        {
            var club = await db.Clubs.SingleAsync(c => c.Id == clubId);
            var court = club.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);
            court.AddBlock(Guid.NewGuid(), new TimeSlot(wienerMittag, wienerMittag.AddHours(1)), BlockReason.Training);
            court.AddBlock(Guid.NewGuid(), new TimeSlot(frueher, frueher.AddHours(1)), BlockReason.Training);
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            var sorted = await db.Set<CourtBlock>()
                .OrderBy(b => b.Period.Start)
                .Select(b => b.Period.Start)
                .ToListAsync();

            Assert.Equal([frueher, londonMorgen], sorted);
        }
    }

    [Fact]
    public async Task Der_Platzname_ist_je_Verein_eindeutig()
    {
        var clubId = await SeedClubAsync();
        var otherClubId = await SeedClubOtherAsync();

        await using (var db = _database.NewContext())
        {
            (await db.Clubs.SingleAsync(c => c.Id == clubId))
                .AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

            // Derselbe Name in einem anderen Verein ist völlig in Ordnung —
            // „Platz 1" gibt es in jedem Verein.
            (await db.Clubs.SingleAsync(c => c.Id == otherClubId))
                .AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Hard, CourtLocation.Indoor);

            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            Assert.Equal(2, await db.Courts.CountAsync());
        }
    }

    private async Task<Guid> SeedClubOtherAsync()
    {
        await using var db = _database.NewContext();
        var club = new Club(Guid.NewGuid(), "TC Nachbarort", "Europe/Vienna");
        db.Clubs.Add(club);
        await db.SaveChangesAsync();
        return club.Id;
    }
}
