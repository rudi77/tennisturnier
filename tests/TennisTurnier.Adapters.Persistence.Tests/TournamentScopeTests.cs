using Microsoft.EntityFrameworkCore;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Adapters.Persistence.Tests;

/// <summary>
/// Der Query-Filter für Turniere. Es gibt genau einen Weg zu einem Turnier: eine
/// Rolle an diesem Turnier. Der zweite — eine Rolle im ausrichtenden Verein —
/// ist mit dem Verein entfallen, und das ist die engere Grenze: wer zwei Vereine
/// verwaltete, sah zuvor alles, was dort je stattgefunden hatte.
///
/// Seit die Plätze am Turnier hängen, gilt derselbe Filter auch für sie und für
/// ihre Zeiten. Diese Klasse hält beides fest — sie hat die Aussagen der
/// früheren <c>ClubScopeQueryFilterTests</c> aufgenommen.
/// </summary>
public sealed class TournamentScopeTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();

    private Guid _tournamentId;
    private Guid _foreignTournamentId;
    private Guid _ownerId;

    public async Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        await using var db = _database.NewContext();

        var template = new FormatTemplate(Guid.NewGuid(), null, BuiltInFormats.Knockout);
        db.FormatTemplates.Add(template);

        var tournament = NewTournament(template.Id, "Clubmeisterschaft");
        var court = tournament.AddCourt(
            Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);
        court.AddWindow(Guid.NewGuid(), Fenster(new DateOnly(2026, 5, 16)));

        var foreign = NewTournament(template.Id, "Fremdes Turnier");
        foreign.AddCourt(Guid.NewGuid(), "Fremder Platz", CourtSurface.Clay, CourtLocation.Outdoor);

        db.Tournaments.AddRange(tournament, foreign);

        await db.SaveChangesAsync();

        _tournamentId = tournament.Id;
        _foreignTournamentId = foreign.Id;
        _ownerId = Guid.NewGuid();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private static TimeSlot Fenster(DateOnly tag) => new(
        new DateTimeOffset(tag.ToDateTime(new TimeOnly(8, 0)), TimeSpan.FromHours(2)),
        new DateTimeOffset(tag.ToDateTime(new TimeOnly(20, 0)), TimeSpan.FromHours(2)));

    private static Tournament NewTournament(Guid templateId, string name)
    {
        var tournament = new Tournament(
            Guid.NewGuid(),
            name,
            new Venue("TC Alpha", null, "Maria Alm", "Europe/Vienna"),
            Discipline.Singles,
            new DateOnly(2026, 5, 16),
            new DateOnly(2026, 5, 17),
            templateId);

        tournament.OpenRegistration();
        return tournament;
    }

    private static UserPrincipal With(params (Role Role, ResourceScope Scope)[] roles)
    {
        var userId = Guid.NewGuid();
        return new UserPrincipal(
            userId,
            roles.Select(r => new RoleAssignment(Guid.NewGuid(), userId, r.Role, r.Scope)).ToList());
    }

    [Fact]
    public async Task Ein_Turnierleiter_ohne_Vereinsrolle_findet_sein_Turnier()
    {
        // Regression: der Filter kannte nur Vereinsrollen. Ein Turnierleiter hat
        // aber ausschließlich eine turniergebundene Rolle — sein eigenes Turnier
        // kam als 404 zurück, obwohl er genau dafür berufen wurde.
        _database.ActingAs = With((Role.TournamentDirector, ResourceScope.Tournament(_tournamentId)));
        await using var db = _database.NewContext();

        var tournament = await db.Tournaments.FirstOrDefaultAsync(t => t.Id == _tournamentId);

        Assert.NotNull(tournament);
        Assert.Equal("Clubmeisterschaft", tournament.Name);
    }

    [Fact]
    public async Task Ein_Turnierleiter_sieht_nur_sein_eigenes_Turnier()
    {
        _database.ActingAs = With((Role.TournamentDirector, ResourceScope.Tournament(_tournamentId)));
        await using var db = _database.NewContext();

        Assert.Equal([_tournamentId], (await db.Tournaments.ToListAsync()).Select(t => t.Id));
        Assert.Null(await db.Tournaments.FirstOrDefaultAsync(t => t.Id == _foreignTournamentId));
    }

    [Fact]
    public async Task Ein_Schiedsrichter_findet_das_Turnier_dessen_Ergebnisse_er_eintragen_soll()
    {
        _database.ActingAs = With((Role.Referee, ResourceScope.Tournament(_tournamentId)));
        await using var db = _database.NewContext();

        Assert.NotNull(await db.Tournaments.FirstOrDefaultAsync(t => t.Id == _tournamentId));
    }

    [Fact]
    public async Task Ein_Turnierleiter_sieht_auch_die_Meldungen_seines_Turniers()
    {
        _database.ActingAs = UserPrincipal.System;
        await using (var setup = _database.NewContext())
        {
            var tournament = await setup.Tournaments.SingleAsync(t => t.Id == _tournamentId);
            var participant = Participant.Single(Guid.NewGuid(), Guid.NewGuid(), "Müller, Anna");
            setup.Participants.Add(participant);
            tournament.Enter(Guid.NewGuid(), participant.Id);
            await setup.SaveChangesAsync();
        }

        _database.ActingAs = With((Role.TournamentDirector, ResourceScope.Tournament(_tournamentId)));
        await using var db = _database.NewContext();

        Assert.Single(await db.Set<TournamentEntry>().ToListAsync());
    }

    [Fact]
    public async Task Ein_Turnierleiter_erreicht_die_Plaetze_seines_Turniers()
    {
        // Die Platzvergabe ist der Grund, aus dem er berufen wurde. Sie hing
        // einmal am Verein und endete für ihn in einem 404 auf fremde
        // Stammdaten; jetzt hängt sie am Turnier und kommt mit ihm.
        _database.ActingAs = With((Role.TournamentDirector, ResourceScope.Tournament(_tournamentId)));
        await using var db = _database.NewContext();

        var tournament = await db.Tournaments.FirstOrDefaultAsync(t => t.Id == _tournamentId);

        Assert.NotNull(tournament);
        Assert.Equal("Platz 1", Assert.Single(tournament.Courts).Name);
    }

    [Fact]
    public async Task Ein_Turnierleiter_sieht_die_Zeiten_der_Plaetze_seines_Turniers()
    {
        // Ohne sie hielte die Spielplanprüfung jeden Platz für dauerhaft
        // geschlossen und meldete zu jeder Zuweisung einen Verstoß.
        _database.ActingAs = With((Role.TournamentDirector, ResourceScope.Tournament(_tournamentId)));
        await using var db = _database.NewContext();

        Assert.Single(await db.Set<CourtWindow>().ToListAsync());
    }

    [Fact]
    public async Task Die_Plaetze_eines_fremden_Turniers_bleiben_verborgen()
    {
        _database.ActingAs = With((Role.TournamentDirector, ResourceScope.Tournament(_tournamentId)));
        await using var db = _database.NewContext();

        Assert.Null(await db.Tournaments.FirstOrDefaultAsync(t => t.Id == _foreignTournamentId));
        Assert.Equal(
            [_tournamentId],
            (await db.Courts.ToListAsync()).Select(c => c.TournamentId).Distinct());
    }

    [Fact]
    public async Task Ein_angemeldeter_Benutzer_ohne_Rolle_sieht_kein_Turnier()
    {
        // Die neue Grenze: es gibt keinen Weg zu einem Turnier, der nicht über
        // eine Rolle an genau diesem Turnier führt. Vorher genügte eine Rolle im
        // ausrichtenden Verein — und wer zwei Vereine verwaltete, sah alles,
        // was dort je stattgefunden hatte.
        var stranger = Guid.NewGuid();
        _database.ActingAs = new UserPrincipal(stranger, []);

        await using var db = _database.NewContext();

        Assert.Empty(await db.Tournaments.ToListAsync());
        Assert.Empty(await db.Courts.ToListAsync());
        Assert.Empty(await db.Set<CourtWindow>().ToListAsync());
    }

    [Fact]
    public async Task Ein_nicht_angemeldeter_Aufrufer_sieht_kein_Turnier()
    {
        _database.ActingAs = UserPrincipal.Anonymous;
        await using var db = _database.NewContext();

        Assert.Empty(await db.Tournaments.ToListAsync());
        Assert.Empty(await db.Set<TournamentEntry>().ToListAsync());
    }

    [Fact]
    public async Task Der_Anmeldelink_umgeht_den_Filter_und_nur_er()
    {
        // Der anonyme Meldeweg braucht genau eine Ausnahme, und sie liegt an
        // genau einer Stelle: der Token ist die Autorisierung. Der normale Weg
        // bleibt für denselben Aufrufer verschlossen — sonst wäre die Ausnahme
        // keine.
        var token = await TokenAsync();

        _database.ActingAs = UserPrincipal.Anonymous;
        await using var db = _database.NewContext();

        Assert.Null(await db.Tournaments.FirstOrDefaultAsync(t => t.Id == _tournamentId));
        Assert.NotNull(await db.Tournaments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Registration.Token == token));
    }

    private async Task<string> TokenAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        await using var setup = _database.NewContext();

        return (await setup.Tournaments.SingleAsync(t => t.Id == _tournamentId)).Registration.Token;
    }

    [Fact]
    public async Task Zwei_Rollen_liefern_beide_Turniere()
    {
        _database.ActingAs = With(
            (Role.TournamentDirector, ResourceScope.Tournament(_tournamentId)),
            (Role.Referee, ResourceScope.Tournament(_foreignTournamentId)));

        await using var db = _database.NewContext();

        Assert.Equal(
            new[] { _tournamentId, _foreignTournamentId }.Order(),
            (await db.Tournaments.ToListAsync()).Select(t => t.Id).Order());
    }

    [Fact]
    public async Task Eine_eigene_Formatvorlage_sieht_nur_ihr_Eigentuemer()
    {
        // Vorlagen hingen am Verein und waren damit für dessen Verwalter
        // sichtbar. Sie hängen jetzt an ihrem Anleger — sonst wären sie für
        // niemanden mehr auffindbar, denn einen Verein gibt es nicht mehr.
        _database.ActingAs = UserPrincipal.System;
        Guid templateId;

        await using (var setup = _database.NewContext())
        {
            var template = new FormatTemplate(Guid.NewGuid(), _ownerId, BuiltInFormats.Swiss);
            setup.FormatTemplates.Add(template);
            await setup.SaveChangesAsync();
            templateId = template.Id;
        }

        _database.ActingAs = new UserPrincipal(_ownerId, []);
        await using (var owner = _database.NewContext())
        {
            Assert.NotNull(await owner.FormatTemplates.FirstOrDefaultAsync(t => t.Id == templateId));

            // Die mitgelieferten Vorlagen stehen daneben weiterhin jedem offen.
            Assert.NotEmpty(await owner.FormatTemplates.Where(t => t.OwnerUserId == null).ToListAsync());
        }

        _database.ActingAs = new UserPrincipal(Guid.NewGuid(), []);
        await using var stranger = _database.NewContext();

        Assert.Null(await stranger.FormatTemplates.FirstOrDefaultAsync(t => t.Id == templateId));
    }

    [Fact]
    public async Task Zwei_gleichzeitige_Aenderungen_an_derselben_Vorlage_werden_erkannt()
    {
        // Ohne Nebenläufigkeitstoken gingen beide durch, schrieben dieselbe neue
        // Version, und ein Turnier, das mit dieser Version einfriert, wiese einen
        // Stand aus, den es nie gab.
        _database.ActingAs = UserPrincipal.System;
        Guid templateId;

        await using (var setup = _database.NewContext())
        {
            var template = new FormatTemplate(Guid.NewGuid(), _ownerId, BuiltInFormats.Knockout);
            setup.FormatTemplates.Add(template);
            await setup.SaveChangesAsync();
            templateId = template.Id;
        }

        await using var first = _database.NewContext();
        await using var second = _database.NewContext();

        var a = await first.FormatTemplates.SingleAsync(t => t.Id == templateId);
        var b = await second.FormatTemplates.SingleAsync(t => t.Id == templateId);

        a.Update(BuiltInFormats.League);
        await first.SaveChangesAsync();

        b.Update(BuiltInFormats.Swiss);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Die_Meldungen_tragen_keinen_zweiten_Fremdschluessel()
    {
        // Regression: AcceptedEntries ist eine berechnete Sicht, wurde von EF aber
        // als zweite Sammelnavigation erkannt. Die Schattenspalte TournamentId1
        // folgte damit einem abgeleiteten Status und wurde beim Rückzug genullt.
        await using var db = _database.NewContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('TournamentEntries')";

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        Assert.DoesNotContain("TournamentId1", columns);
        Assert.Contains("TournamentId", columns);
    }
}
