using Microsoft.EntityFrameworkCore;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Adapters.Persistence.Tests;

public sealed class TournamentPersistenceTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();

    private Guid _clubId;
    private Guid _templateId;
    private readonly List<Guid> _participantIds = [];

    public async Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        await using var db = _database.NewContext();

        var club = new Club(Guid.NewGuid(), "TC Musterstadt", "Europe/Vienna");
        db.Clubs.Add(club);

        var template = new FormatTemplate(Guid.NewGuid(), club.Id, BuiltInFormats.GroupThenKnockout);
        db.FormatTemplates.Add(template);

        foreach (var name in new[] { "Müller, Anna", "Berger, Eva", "Huber, Lisa", "Wagner, Sarah" })
        {
            var player = new Player(Guid.NewGuid(), name.Split(", ")[1], name.Split(", ")[0]);
            var participant = Participant.Single(Guid.NewGuid(), player.Id, name);
            db.Players.Add(player);
            db.Participants.Add(participant);
            _participantIds.Add(participant.Id);
        }

        await db.SaveChangesAsync();

        _clubId = club.Id;
        _templateId = template.Id;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private Tournament NewTournament() => new(
        Guid.NewGuid(), _clubId, "Clubmeisterschaft 2026",
        new DateOnly(2026, 5, 16), new DateOnly(2026, 5, 17), _templateId);

    private async Task<Guid> SeedTournamentAsync(bool withDraw)
    {
        await using var db = _database.NewContext();
        var tournament = NewTournament();

        tournament.OpenRegistration();
        foreach (var participantId in _participantIds)
        {
            var entry = tournament.Enter(Guid.NewGuid(), participantId);
            tournament.Accept(entry.Id);
        }

        if (withDraw)
        {
            tournament.CloseRegistration();
            var template = await db.FormatTemplates.SingleAsync(t => t.Id == _templateId);
            tournament.GenerateDraw(template.Definition, template.Version);
        }

        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();

        return tournament.Id;
    }

    [Fact]
    public async Task Ein_Turnier_mit_Meldungen_ueberlebt_den_Neuaufbau()
    {
        var id = await SeedTournamentAsync(withDraw: false);

        await using var db = _database.NewContext();
        var tournament = await db.Tournaments.SingleAsync(t => t.Id == id);

        Assert.Equal("Clubmeisterschaft 2026", tournament.Name);
        Assert.Equal(TournamentState.RegistrationOpen, tournament.State);
        Assert.Equal(4, tournament.Entries.Count);
        Assert.Equal(4, tournament.AcceptedEntries.Count);
    }

    [Fact]
    public async Task Ohne_Auslosung_ist_das_gespeicherte_Format_leer()
    {
        var id = await SeedTournamentAsync(withDraw: false);

        await using var db = _database.NewContext();

        Assert.Null((await db.Tournaments.SingleAsync(t => t.Id == id)).Format);
    }

    [Fact]
    public async Task Die_eingefrorene_Formatdefinition_uebersteht_die_Rundreise_durch_JSON()
    {
        // Das ist der Kern von ADR-0001 auf der Persistenzebene: eine Definition,
        // die beim Speichern Details verliert, wäre als eingefrorener Stand
        // wertlos.
        var id = await SeedTournamentAsync(withDraw: true);

        await using var db = _database.NewContext();
        var snapshot = (await db.Tournaments.SingleAsync(t => t.Id == id)).Format;

        Assert.NotNull(snapshot);
        Assert.Equal(_templateId, snapshot.TemplateId);
        Assert.Equal(1, snapshot.TemplateVersion);

        var definition = snapshot.Definition;
        Assert.Equal(BuiltInFormats.GroupThenKnockoutId, definition.Id);
        Assert.Equal(2, definition.Phases.Count);

        var groups = definition.Phases[0];
        Assert.Equal(PhaseFormatKind.RoundRobin, groups.Format);
        Assert.Equal(4, groups.GroupCount);
        Assert.Equal(2, groups.Scoring.Win);
        Assert.Equal(
            [Tiebreaker.DirectEncounter, Tiebreaker.SetRatio, Tiebreaker.GameRatio, Tiebreaker.Lot],
            groups.Tiebreakers);

        var final = definition.Phases[1];
        Assert.Equal(PhaseFormatKind.Knockout, final.Format);
        Assert.True(final.ThirdPlaceMatch);
        Assert.Equal(1, final.Qualification!.FromPhase);
        Assert.Equal(QualificationRule.TopNPerGroup, final.Qualification.Rule);
        Assert.Equal(SeedingRule.CrossGroup, final.Qualification.Seeding);

        Assert.Equal(FinalSetMode.MatchTiebreak10, definition.MatchFormat.FinalSetMode);
    }

    [Fact]
    public async Task Enums_stehen_als_Namen_in_der_Datenbank()
    {
        // Als Zahlen wären gespeicherte Definitionen weder lesbar noch
        // vergleichbar, und das Einfügen eines Enum-Werts würde sie umdeuten.
        await SeedTournamentAsync(withDraw: true);

        await using var db = _database.NewContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT FormatSnapshot, State FROM Tournaments";

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        var json = reader.GetString(0);
        Assert.Contains("\"RoundRobin\"", json, StringComparison.Ordinal);
        Assert.Contains("\"TopNPerGroup\"", json, StringComparison.Ordinal);
        Assert.Equal("DrawGenerated", reader.GetString(1));
    }

    [Fact]
    public async Task Ein_unveraendertes_Turnier_erzeugt_keinen_Schreibvorgang()
    {
        // Ohne eigenen Wertvergleicher hielte EF Core die geladene Definition für
        // verändert und schriebe bei jedem Laden zurück — auf SQLite, das
        // datenbankweit serialisiert, eine spürbare Last aus dem Nichts.
        var id = await SeedTournamentAsync(withDraw: true);

        await using var db = _database.NewContext();
        var tournament = await db.Tournaments.SingleAsync(t => t.Id == id);
        var versionBefore = tournament.Version;

        var written = await db.SaveChangesAsync();

        Assert.Equal(0, written);
        Assert.Equal(versionBefore, tournament.Version);
    }

    [Fact]
    public async Task Ein_Teilnehmer_behaelt_seine_Spieler()
    {
        await using var db = _database.NewContext();

        var participants = await db.Participants.ToListAsync();

        Assert.Equal(4, participants.Count);
        Assert.All(participants, p => Assert.Single(p.PlayerIds));
        Assert.All(participants, p => Assert.False(p.IsTeam));
    }

    [Fact]
    public async Task Ein_Doppel_behaelt_beide_Spieler_in_ihrer_Reihenfolge()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var id = Guid.NewGuid();

        await using (var db = _database.NewContext())
        {
            db.Players.Add(new Player(first, "Anna", "Müller"));
            db.Players.Add(new Player(second, "Eva", "Berger"));
            db.Participants.Add(Participant.Team(id, first, second, "Müller / Berger"));
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            var team = await db.Participants.SingleAsync(p => p.Id == id);

            Assert.True(team.IsTeam);
            Assert.Equal([first, second], team.PlayerIds);
        }
    }

    [Fact]
    public async Task Zwei_gleichzeitige_Aenderungen_am_selben_Turnier_werden_erkannt()
    {
        // Zwei Schiedsrichter, die dasselbe Ergebnis eintragen, sind am
        // Turniertag der Normalfall — nicht der Randfall.
        var id = await SeedTournamentAsync(withDraw: false);

        await using var first = _database.NewContext();
        await using var second = _database.NewContext();

        var a = await first.Tournaments.SingleAsync(t => t.Id == id);
        var b = await second.Tournaments.SingleAsync(t => t.Id == id);

        a.Rename("Zuerst gespeichert");
        await first.SaveChangesAsync();

        b.Rename("Danach gespeichert");

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Kontaktdaten_eines_Spielers_ueberleben_den_Neuaufbau()
    {
        var id = Guid.NewGuid();
        var contact = new PlayerContact("anna@example.invalid", "+43 1 234567", new DateOnly(1990, 3, 14));

        await using (var db = _database.NewContext())
        {
            db.Players.Add(new Player(id, "Anna", "Müller", contact));
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            Assert.Equal(contact, (await db.Players.SingleAsync(p => p.Id == id)).Contact);
        }
    }

    [Fact]
    public async Task Eine_Vorlage_uebersteht_den_Neuaufbau_mit_ihrer_Version()
    {
        await using (var db = _database.NewContext())
        {
            var template = await db.FormatTemplates.SingleAsync(t => t.Id == _templateId);
            template.Update(BuiltInFormats.League);
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            var template = await db.FormatTemplates.SingleAsync(t => t.Id == _templateId);

            Assert.Equal(2, template.Version);
            Assert.Equal(BuiltInFormats.LeagueId, template.Definition.Id);
            Assert.Equal(2, template.Definition.Phases[0].Encounters);
        }
    }
}
