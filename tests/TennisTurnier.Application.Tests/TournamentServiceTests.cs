using TennisTurnier.Application.Common;
using TennisTurnier.Application.Tests.Fakes;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tests;

public sealed class TournamentServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ClubId = Guid.NewGuid();

    private readonly MutableUserContext _userContext = new();
    private readonly InMemoryTournamentRepository _tournaments;
    private readonly InMemoryFormatTemplateRepository _templates = new();
    private readonly InMemoryPlayerRepository _players = new();
    private readonly InMemoryPhaseRepository _phaseRepository = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly TournamentService _service;
    private readonly FormatTemplate _template;

    public TournamentServiceTests()
    {
        _tournaments = new InMemoryTournamentRepository(_userContext);
        _service = new TournamentService(
            _tournaments,
            _templates,
            _players,
            new DrawBuilder(_phaseRepository, _players),
            _unitOfWork,
            _userContext);
        _template = _templates.Seed(new FormatTemplate(Guid.NewGuid(), ClubId, BuiltInFormats.Knockout));

        ActAsClubAdmin();
    }

    private void ActAs(params RoleAssignment[] assignments) =>
        _userContext.Current = new UserPrincipal(UserId, assignments);

    private void ActAsClubAdmin() =>
        ActAs(new RoleAssignment(Guid.NewGuid(), UserId, Role.ClubAdmin, ResourceScope.Club(ClubId)));

    private static CreateTournamentRequest NewRequest(Guid templateId) => new(
        "Clubmeisterschaft 2026", new DateOnly(2026, 5, 16), new DateOnly(2026, 5, 17), templateId);

    private Guid SeedParticipant(string name)
    {
        var participant = Participant.Single(Guid.NewGuid(), Guid.NewGuid(), name);
        _players.Seed(participant);
        return participant.Id;
    }

    private async Task<Guid> CreateWithTwoEntriesAsync()
    {
        var id = await _service.CreateAsync(ClubId, NewRequest(_template.Id));
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
            () => _service.CreateAsync(ClubId, NewRequest(Guid.NewGuid())));
    }

    [Fact]
    public async Task Ohne_Berechtigung_im_Verein_laesst_sich_kein_Turnier_anlegen()
    {
        ActAs(new RoleAssignment(Guid.NewGuid(), UserId, Role.Player, ResourceScope.Club(ClubId)));

        await Assert.ThrowsAsync<AccessDeniedException>(
            () => _service.CreateAsync(ClubId, NewRequest(_template.Id)));
    }

    [Fact]
    public async Task Ein_Turnier_ausserhalb_des_Scopes_wirkt_wie_nicht_vorhanden()
    {
        var id = await _service.CreateAsync(ClubId, NewRequest(_template.Id));
        ActAs(new RoleAssignment(Guid.NewGuid(), UserId, Role.ClubAdmin, ResourceScope.Club(Guid.NewGuid())));

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(id));
    }

    [Fact]
    public async Task Der_Turnierleiter_darf_sein_Turnier_verwalten_ohne_Vereinsrolle()
    {
        // Beide Wege sind zulässig — Turnierleiter des Turniers oder
        // Administrator des Vereins. Der Anwendungsfall kennt beide, statt die
        // Rangfolge an die Aufrufstelle zu delegieren.
        var id = await _service.CreateAsync(ClubId, NewRequest(_template.Id));

        ActAs(
            new RoleAssignment(Guid.NewGuid(), UserId, Role.Player, ResourceScope.Club(ClubId)),
            new RoleAssignment(Guid.NewGuid(), UserId, Role.TournamentDirector, ResourceScope.Tournament(id)));

        await _service.OpenRegistrationAsync(id);

        Assert.Equal(TournamentState.RegistrationOpen, (await _service.GetAsync(id)).State);
    }

    [Fact]
    public async Task Eine_Meldung_setzt_einen_vorhandenen_Teilnehmer_voraus()
    {
        var id = await _service.CreateAsync(ClubId, NewRequest(_template.Id));
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
    public async Task Die_Liste_zeigt_nur_Turniere_des_angefragten_Vereins()
    {
        await _service.CreateAsync(ClubId, NewRequest(_template.Id));

        var otherClub = Guid.NewGuid();
        ActAs(
            new RoleAssignment(Guid.NewGuid(), UserId, Role.ClubAdmin, ResourceScope.Club(ClubId)),
            new RoleAssignment(Guid.NewGuid(), UserId, Role.ClubAdmin, ResourceScope.Club(otherClub)));
        await _service.CreateAsync(otherClub, NewRequest(_template.Id));

        Assert.Single(await _service.ListAsync(ClubId));
        Assert.Single(await _service.ListAsync(otherClub));
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
            new DrawBuilder(_phaseRepository, _players),
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
        await _service.ListAsync(ClubId);

        Assert.Equal(before, _unitOfWork.SavedChanges);
    }

    [Fact]
    public async Task Jeder_Zustandsuebergang_wird_gespeichert()
    {
        var id = await _service.CreateAsync(ClubId, NewRequest(_template.Id));
        var before = _unitOfWork.SavedChanges;

        await _service.OpenRegistrationAsync(id);
        await _service.CloseRegistrationAsync(id);

        Assert.Equal(before + 2, _unitOfWork.SavedChanges);
    }
}
