using TennisTurnier.Adapters.Persistence.Sqlite;
using TennisTurnier.Adapters.Persistence.Sqlite.Repositories;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Adapters.Persistence.Tests;

/// <summary>
/// Die Turnierliste gegen echtes SQLite.
///
/// Sie lud einmal das ganze Aggregat: Plätze, deren Zeiten und alle Meldungen
/// hängen als AutoInclude daran, und in einer Abfrage ergibt das ihr Produkt —
/// acht Plätze mal vier Zeitfenster mal vierundsechzig Meldungen sind
/// zweitausend Zeilen für eine Kachel, auf der ein Name, ein Datum und eine
/// Zahl stehen.
///
/// Was die Liste jetzt zählt, rechnet die Datenbank. Diese Tests halten fest,
/// dass dabei dasselbe herauskommt wie beim Aggregat — eine Projektion, die
/// anders zählt, wäre schlimmer als eine langsame Abfrage.
/// </summary>
public sealed class TurnierlisteTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();

    public Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private static TimeSlot Fenster(DateOnly tag) => new(
        new DateTimeOffset(tag.ToDateTime(new TimeOnly(8, 0)), TimeSpan.FromHours(2)),
        new DateTimeOffset(tag.ToDateTime(new TimeOnly(20, 0)), TimeSpan.FromHours(2)));

    private static Tournament Turnier(Guid templateId, string name, DateOnly? beginn) =>
        new(
            Guid.NewGuid(),
            name,
            new Venue("TC Test", null, "Maria Alm", "Europe/Vienna"),
            Discipline.Singles,
            beginn,
            beginn,
            templateId);

    /// <summary>
    /// Ein Turnier mit zwei Plätzen, je zwei Zeitfenstern und vier Meldungen,
    /// von denen zwei angenommen sind.
    ///
    /// Genau die Form, an der sich das Produkt zeigt — und zugleich die, an der
    /// eine falsch gezählte Meldung auffiele.
    /// </summary>
    private static void Ausstatten(Tournament tournament, TennisTurnierDbContext db)
    {
        foreach (var name in new[] { "Platz 1", "Platz 2" })
        {
            var court = tournament.AddCourt(
                Guid.NewGuid(), name, CourtSurface.Clay, CourtLocation.Outdoor);

            court.AddWindow(Guid.NewGuid(), Fenster(new DateOnly(2026, 5, 16)));
            court.AddWindow(Guid.NewGuid(), Fenster(new DateOnly(2026, 5, 17)));
        }

        tournament.OpenRegistration();

        var meldungen = new List<TournamentEntry>();

        for (var i = 0; i < 4; i++)
        {
            var participant = Participant.Single(Guid.NewGuid(), Guid.NewGuid(), $"Spielerin {i + 1}");
            db.Participants.Add(participant);

            meldungen.Add(tournament.Enter(Guid.NewGuid(), participant.Id));
        }

        tournament.Accept(meldungen[0].Id);
        tournament.Accept(meldungen[1].Id);
    }

    [Fact]
    public async Task Die_Liste_zaehlt_die_angenommenen_Meldungen_wie_das_Aggregat()
    {
        await using var db = _database.NewContext();

        var template = new FormatTemplate(Guid.NewGuid(), null, BuiltInFormats.Knockout);
        db.FormatTemplates.Add(template);

        var tournament = Turnier(template.Id, "Clubmeisterschaft", new DateOnly(2026, 5, 16));
        Ausstatten(tournament, db);

        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();

        await using var frisch = _database.NewContext();
        var liste = await new TournamentRepository(frisch).ListForCallerAsync();

        var kopf = Assert.Single(liste);

        Assert.Equal(tournament.Id, kopf.Id);
        Assert.Equal("Clubmeisterschaft", kopf.Name);
        Assert.Equal("TC Test", kopf.VenueName);
        Assert.Equal(Discipline.Singles, kopf.Discipline);
        Assert.Equal(new DateOnly(2026, 5, 16), kopf.StartsOn);
        Assert.Equal(TournamentState.RegistrationOpen, kopf.State);
        Assert.Equal(SchedulingMode.Planning, kopf.SchedulingMode);
        Assert.False(kopf.IsPublic);

        // Vier Meldungen, zwei davon angenommen — und genau die zählt das
        // Aggregat auch.
        Assert.Equal(2, kopf.AcceptedEntries);
        Assert.Equal(tournament.AcceptedEntries.Count, kopf.AcceptedEntries);
    }

    [Fact]
    public async Task Ohne_Termin_steht_ein_Turnier_vorn()
    {
        // Seit der Termin optional ist, hat ein frisch angelegtes Turnier
        // keinen — und SQLite sortiert NULL unter jeden Wert. Es stand damit
        // hinter jedem vergangenen, und die Oberfläche wählt den ersten Eintrag
        // vor.
        await using var db = _database.NewContext();

        var template = new FormatTemplate(Guid.NewGuid(), null, BuiltInFormats.Knockout);
        db.FormatTemplates.Add(template);

        db.Tournaments.Add(Turnier(template.Id, "Mit Termin", new DateOnly(2026, 5, 16)));
        db.Tournaments.Add(Turnier(template.Id, "Ohne Termin", null));

        await db.SaveChangesAsync();

        await using var frisch = _database.NewContext();
        var liste = await new TournamentRepository(frisch).ListForCallerAsync();

        Assert.Equal(["Ohne Termin", "Mit Termin"], liste.Select(k => k.Name));
    }
}
