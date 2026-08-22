using TennisTurnier.Application.Common;
using TennisTurnier.Domain.Common;
using TennisTurnier.Application.Tests.Fakes;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tests;

public sealed class TournamentServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly MutableUserContext _userContext = new();
    private readonly InMemoryTournamentRepository _tournaments;
    private readonly InMemoryFormatTemplateRepository _templates;
    private readonly InMemoryPlayerRepository _players = new();
    private readonly InMemoryPhaseRepository _phaseRepository = new();
    private readonly InMemoryCourtAssignmentRepository _assignments = new();
    private readonly InMemoryProjectionStore _projections = new();
    private readonly InMemoryRoleAssignmentRepository _roles = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly RecordingPublicViewService _publicView = new();
    private readonly TournamentService _service;
    private readonly FormatTemplate _template;

    public TournamentServiceTests()
    {
        _tournaments = new InMemoryTournamentRepository(_userContext);
        _templates = new InMemoryFormatTemplateRepository(_userContext);
        _publicView.SaveCount = () => _unitOfWork.SavedChanges;
        _service = new TournamentService(
            _tournaments,
            _templates,
            _players,
            _roles,
            _phaseRepository,
            _assignments,
            _projections,
            new DrawBuilder(_phaseRepository, _players),
            _publicView,
            _unitOfWork,
            _userContext);
        _template = _templates.Seed(new FormatTemplate(Guid.NewGuid(), UserId, BuiltInFormats.Knockout));

        ActAsOrganizer();
    }

    private void ActAs(params RoleAssignment[] assignments) =>
        _userContext.Current = new UserPrincipal(UserId, assignments);

    /// <summary>
    /// Der Normalfall, seit der Verein weg ist: wer sich anmeldet, ist
    /// Veranstalter und darf ausschreiben. Alles Weitere folgt aus der
    /// Turnierleiterrolle, die er beim Anlegen bekommt.
    /// </summary>
    private void ActAsOrganizer() =>
        ActAs(new RoleAssignment(Guid.NewGuid(), UserId, Role.Organizer, ResourceScope.Global));

    /// <summary>
    /// Der Anleger führt sein Turnier — im echten Ablauf trägt die
    /// Rollenzuweisung aus <c>CreateAsync</c> das nach. Der Fake spiegelt keine
    /// Zuweisungen in den Benutzerkontext zurück; das tut hier dieser Aufruf.
    /// </summary>
    private void ActAsDirectorOf(Guid tournamentId) =>
        ActAs(
            new RoleAssignment(Guid.NewGuid(), UserId, Role.Organizer, ResourceScope.Global),
            new RoleAssignment(
                Guid.NewGuid(), UserId, Role.TournamentDirector, ResourceScope.Tournament(tournamentId)));

    private static CreateTournamentRequest NewRequest(Guid templateId) => new(
        "Clubmeisterschaft 2026",
        "TC Maria Alm",
        null,
        "Maria Alm",
        "Europe/Vienna",
        Discipline.Singles,
        new DateOnly(2026, 5, 16),
        new DateOnly(2026, 5, 17),
        templateId);

    private Guid SeedParticipant(string name)
    {
        var participant = Participant.Single(Guid.NewGuid(), Guid.NewGuid(), name);
        _players.Seed(participant);
        return participant.Id;
    }

    /// <summary>
    /// Legt ein Turnier an und übernimmt die dabei entstandene Turnierleiterrolle
    /// in den Benutzerkontext.
    ///
    /// Im Betrieb tut das die Middleware beim nächsten Request; hier steht es
    /// ausdrücklich da, weil sonst jeder Folgeaufruf am Query-Filter scheiterte —
    /// und genau das wäre auch die Wirkung, wenn <c>CreateAsync</c> die Zuweisung
    /// vergäße.
    /// </summary>
    private async Task<Guid> AnlegenAsync(Guid? templateId = null)
    {
        var id = await _service.CreateAsync(NewRequest(templateId ?? _template.Id));
        ActAsDirectorOf(id);

        return id;
    }

    private async Task<Guid> CreateWithTwoEntriesAsync()
    {
        var id = await AnlegenAsync();
        await _service.OpenRegistrationAsync(id);

        foreach (var name in new[] { "Müller, Anna", "Berger, Eva" })
        {
            var entryId = await _service.EnterAsync(id, new EnterTournamentRequest(SeedParticipant(name), null));
            await _service.AcceptAsync(id, entryId);
        }

        return id;
    }

    [Fact]
    public async Task Ein_Turnier_braucht_eine_vorhandene_Formatvorlage()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateAsync(NewRequest(Guid.NewGuid())));
    }

    [Fact]
    public async Task Ohne_Berechtigung_laesst_sich_kein_Turnier_anlegen()
    {
        // Ein Schiedsrichter an irgendeinem Turnier ist kein Veranstalter.
        ActAs(new RoleAssignment(
            Guid.NewGuid(), UserId, Role.Referee, ResourceScope.Tournament(Guid.NewGuid())));

        await Assert.ThrowsAsync<AccessDeniedException>(
            () => _service.CreateAsync(NewRequest(_template.Id)));
    }

    [Fact]
    public async Task Ein_Turnier_ausserhalb_des_Scopes_wirkt_wie_nicht_vorhanden()
    {
        var id = await _service.CreateAsync(NewRequest(_template.Id));

        ActAs(new RoleAssignment(
            Guid.NewGuid(), UserId, Role.TournamentDirector, ResourceScope.Tournament(Guid.NewGuid())));

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(id));
    }

    [Fact]
    public async Task Wer_ein_Turnier_anlegt_wird_sein_Turnierleiter()
    {
        // Die Zuweisung entsteht in derselben Arbeitseinheit wie das Turnier.
        // Ohne sie wäre es für seinen eigenen Anleger im nächsten Augenblick
        // nicht mehr auffindbar — und ohne Rolle gäbe es keinen Weg zurück.
        var id = await _service.CreateAsync(NewRequest(_template.Id));

        var assignment = Assert.Single(_roles.Assignments);

        Assert.Equal(UserId, assignment.UserId);
        Assert.Equal(Role.TournamentDirector, assignment.Role);
        Assert.Equal(ResourceScope.Tournament(id), assignment.Scope);
    }

    [Fact]
    public async Task Ein_frisch_angelegtes_Turnier_traegt_Ort_und_Disziplin()
    {
        // Beides stand vorher nirgends: der Ort gar nicht, die Disziplin nur
        // implizit in dem, was jemand als Teilnehmer anlegte.
        var id = await AnlegenAsync();

        var detail = await _service.GetAsync(id);

        Assert.Equal("TC Maria Alm", detail.Venue.Name);
        Assert.Equal("Europe/Vienna", detail.Venue.TimeZoneId);
        Assert.Equal(Discipline.Singles, detail.Discipline);
        Assert.Empty(detail.Courts);
    }

    [Fact]
    public async Task Der_Turnierleiter_darf_sein_Turnier_verwalten()
    {
        var id = await _service.CreateAsync(NewRequest(_template.Id));

        ActAs(new RoleAssignment(
            Guid.NewGuid(), UserId, Role.TournamentDirector, ResourceScope.Tournament(id)));

        await _service.OpenRegistrationAsync(id);

        Assert.Equal(TournamentState.RegistrationOpen, (await _service.GetAsync(id)).State);
    }

    [Fact]
    public async Task Eine_Meldung_setzt_einen_vorhandenen_Teilnehmer_voraus()
    {
        var id = await AnlegenAsync();
        await _service.OpenRegistrationAsync(id);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.EnterAsync(id, new EnterTournamentRequest(Guid.NewGuid(), null)));
    }

    [Fact]
    public async Task Die_Auslosung_friert_die_Definition_der_Vorlage_ein()
    {
        var id = await CreateWithTwoEntriesAsync();
        await _service.CloseRegistrationAsync(id);

        await _service.GenerateDrawAsync(id);

        var detail = await _service.GetAsync(id);
        Assert.NotNull(detail.Format);
        Assert.Equal(_template.Id, detail.Format.TemplateId);
        Assert.Equal(1, detail.Format.TemplateVersion);
        Assert.Equal(BuiltInFormats.KnockoutId, detail.Format.Definition.Id);
    }

    [Fact]
    public async Task Die_Detailansicht_nennt_die_Teilnehmer_beim_Namen()
    {
        var id = await CreateWithTwoEntriesAsync();

        var detail = await _service.GetAsync(id);

        Assert.Equal(
            ["Berger, Eva", "Müller, Anna"],
            detail.Entries.Select(e => e.ParticipantName).Order());
    }

    [Fact]
    public async Task Die_Liste_zeigt_nur_die_eigenen_Turniere()
    {
        var id = await AnlegenAsync();

        var summary = Assert.Single(await _service.ListMineAsync());
        Assert.Equal(id, summary.Id);
        Assert.Equal("TC Maria Alm", summary.VenueName);

        // Ein anderer Veranstalter — angemeldet, aber ohne Rolle an diesem
        // Turnier. Die Liste ist der Einstieg in die Oberfläche; stünde hier ein
        // fremdes Turnier, wäre der ganze Filter wertlos.
        var anderer = Guid.NewGuid();
        _userContext.Current = new UserPrincipal(
            anderer,
            [new RoleAssignment(Guid.NewGuid(), anderer, Role.Organizer, ResourceScope.Global)]);

        Assert.Empty(await _service.ListMineAsync());
    }

    /// <summary>
    /// Sichtbar heißt nicht verwendbar.
    ///
    /// Die mitgelieferten Vorlagen sieht jeder. Nähme ein Turnier die eigene
    /// Vorlage eines fremden Benutzers, hinge sein Format bis zur Auslosung an
    /// einer Definition, die ein anderer noch ändern kann — und die Änderung
    /// fröre mit der Auslosung ein.
    /// </summary>
    [Fact]
    public async Task Ein_Turnier_nimmt_keine_Vorlage_eines_fremden_Benutzers()
    {
        var fremde = _templates.Seed(
            new FormatTemplate(Guid.NewGuid(), Guid.NewGuid(), BuiltInFormats.League));

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateAsync(NewRequest(fremde.Id)));
    }

    [Fact]
    public async Task Eine_mitgelieferte_Vorlage_steht_jedem_offen()
    {
        var builtIn = _templates.Seed(new FormatTemplate(Guid.NewGuid(), null, BuiltInFormats.League));

        Assert.NotEqual(Guid.Empty, await _service.CreateAsync(NewRequest(builtIn.Id)));
    }

    [Fact]
    public async Task Eine_verlorene_Vorlage_verhindert_die_Auslosung_mit_klarer_Meldung()
    {
        // Kann eintreten, wenn eine Vorlage gelöscht wurde, nachdem ein Turnier
        // sie referenziert hat. Besser ein „nicht gefunden" als ein Absturz.
        var id = await CreateWithTwoEntriesAsync();
        await _service.CloseRegistrationAsync(id);

        var withoutTemplate = new TournamentService(
            _tournaments,
            new InMemoryFormatTemplateRepository(),
            _players,
            _roles,
            _phaseRepository,
            _assignments,
            _projections,
            new DrawBuilder(_phaseRepository, _players),
            _publicView,
            _unitOfWork,
            _userContext);

        await Assert.ThrowsAsync<NotFoundException>(() => withoutTemplate.GenerateDrawAsync(id));
    }

    [Fact]
    public async Task Lesende_Aufrufe_speichern_nicht()
    {
        var id = await CreateWithTwoEntriesAsync();
        var before = _unitOfWork.SavedChanges;

        await _service.GetAsync(id);
        await _service.ListMineAsync();

        Assert.Equal(before, _unitOfWork.SavedChanges);
    }

    [Fact]
    public async Task Jeder_Zustandsuebergang_wird_gespeichert()
    {
        var id = await AnlegenAsync();
        var before = _unitOfWork.SavedChanges;

        await _service.OpenRegistrationAsync(id);
        await _service.CloseRegistrationAsync(id);

        Assert.Equal(before + 2, _unitOfWork.SavedChanges);
    }

    [Fact]
    public async Task Lesende_Aufrufe_bauen_die_oeffentliche_Ansicht_nicht_neu()
    {
        var id = await CreateWithTwoEntriesAsync();
        var before = _publicView.Rebuilt.Count;

        await _service.GetAsync(id);
        await _service.ListMineAsync();

        Assert.Equal(before, _publicView.Rebuilt.Count);
    }

    [Fact]
    public async Task Die_oeffentliche_Ansicht_entsteht_vor_dem_Speichern()
    {
        // Sie gehört in dieselbe Einheit der Arbeit wie die Änderung, aus der sie
        // folgt. Liefe sie danach als eigener Speichervorgang, läse sie einen
        // Stand, den parallele Anfragen inzwischen überholt haben, schriebe ihn
        // als letzter fest — und meldete dem Aufrufer obendrein einen Konflikt
        // für etwas, das längst gespeichert ist.
        var id = await CreateWithTwoEntriesAsync();

        var savesBefore = _unitOfWork.SavedChanges;
        await _service.CloseRegistrationAsync(id);

        Assert.Equal(savesBefore, _publicView.SavesBeforeLastRebuild);
        Assert.Equal(savesBefore + 1, _unitOfWork.SavedChanges);
    }

    [Fact]
    public async Task Jede_schreibende_Handlung_baut_die_oeffentliche_Ansicht_neu()
    {
        // Der Neuaufbau hängt an jeder einzelnen Handlung, und genau darin liegt
        // die Gefahr: der nächste Anwendungsfall vergisst ihn, und die Ansicht
        // steht still, ohne dass irgendetwas fehlschlägt (ADR-0003).
        var id = await CreateWithTwoEntriesAsync();
        _ = _publicView.Rebuilt;

        await _service.CloseRegistrationAsync(id);
        await _service.GenerateDrawAsync(id);
        await _service.ReopenRegistrationAsync(id);

        Assert.All(_publicView.Rebuilt, rebuilt => Assert.Equal(id, rebuilt));
        Assert.True(_publicView.Rebuilt.Count >= 3);
    }

    [Fact]
    public async Task Der_Systemkontext_wird_nicht_zur_Turnierleitung()
    {
        // Ein Turnier, das im Zuge einer Wartung entsteht, gehört keinem
        // Menschen. Eine Rollenzuweisung für Guid.Empty wäre keine — und stünde
        // danach in jeder Rollenübersicht.
        // Über eine mitgelieferte Vorlage: eine eigene gehört jemandem, und der
        // Systemkontext sieht sie nicht.
        var mitgeliefert = _templates.Seed(
            new FormatTemplate(Guid.NewGuid(), ownerUserId: null, BuiltInFormats.Knockout));

        _userContext.Current = UserPrincipal.System;

        var id = await _service.CreateAsync(NewRequest(mitgeliefert.Id));

        Assert.NotEqual(Guid.Empty, id);
        Assert.Empty(_roles.Assignments);
    }

    [Fact]
    public async Task Ein_Format_ohne_Implementierung_laesst_sich_nicht_auslosen()
    {
        // Eine Vorlage kann eine Formatart nennen, die diese Fassung nicht
        // umsetzt — etwa nach einem Rückbau. Die Absage kommt vor dem Auslosen,
        // nicht mittendrin: ein halb gebauter Draw wäre nicht zu reparieren.
        var unbekannt = _templates.Seed(new FormatTemplate(
            Guid.NewGuid(),
            UserId,
            BuiltInFormats.Knockout with
            {
                Id = "unbekanntes-format",
                Name = "Unbekanntes Format",
                Phases = [BuiltInFormats.Knockout.Phases[0] with { Format = (PhaseFormatKind)99 }],
            }));

        var id = await AnlegenAsync(unbekannt.Id);
        await _service.OpenRegistrationAsync(id);

        foreach (var name in new[] { "Müller, Anna", "Berger, Eva" })
        {
            var entryId = await _service.EnterAsync(id, new EnterTournamentRequest(SeedParticipant(name), null));
            await _service.AcceptAsync(id, entryId);
        }

        await _service.CloseRegistrationAsync(id);

        var fehler = await Assert.ThrowsAsync<DomainException>(() => _service.GenerateDrawAsync(id));

        Assert.Contains("noch nicht umgesetzt", fehler.Message, StringComparison.Ordinal);
    }
}
