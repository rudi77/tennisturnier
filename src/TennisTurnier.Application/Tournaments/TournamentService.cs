using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

public sealed class TournamentService : ITournamentService
{
    private readonly ITournamentRepository _tournaments;
    private readonly IFormatTemplateRepository _templates;
    private readonly IPlayerRepository _players;
    private readonly DrawBuilder _drawBuilder;
    private readonly IPublicViewService _publicView;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;

    public TournamentService(
        ITournamentRepository tournaments,
        IFormatTemplateRepository templates,
        IPlayerRepository players,
        DrawBuilder drawBuilder,
        IPublicViewService publicView,
        IUnitOfWork unitOfWork,
        IUserContext userContext)
    {
        _tournaments = tournaments;
        _templates = templates;
        _players = players;
        _drawBuilder = drawBuilder;
        _publicView = publicView;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
    }

    private UserPrincipal User => _userContext.Current;

    public async Task<Guid> CreateAsync(
        Guid clubId,
        CreateTournamentRequest request,
        CancellationToken cancellationToken = default)
    {
        User.Require(Permission.ManageTournament, ResourceScope.Club(clubId));

        var template = await _templates.FindAsync(request.FormatTemplateId, cancellationToken)
            ?? throw new NotFoundException("Formatvorlage", request.FormatTemplateId);

        var tournament = new Tournament(
            Guid.NewGuid(), clubId, request.Name, request.StartsOn, request.EndsOn, template.Id);

        _tournaments.Add(tournament);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return tournament.Id;
    }

    public async Task<IReadOnlyList<TournamentSummary>> ListAsync(
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        var tournaments = await _tournaments.ListByClubAsync(clubId, cancellationToken);

        return tournaments
            .Select(t => new TournamentSummary(
                t.Id, t.Name, t.StartsOn, t.EndsOn, t.State, t.SchedulingMode, t.AcceptedEntries.Count))
            .ToList();
    }

    public async Task<TournamentDetail> GetAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await Load(tournamentId, cancellationToken);
        var names = await ParticipantNamesAsync(tournament, cancellationToken);

        return new TournamentDetail(
            tournament.Id,
            tournament.ClubId,
            tournament.Name,
            tournament.StartsOn,
            tournament.EndsOn,
            tournament.State,
            tournament.SchedulingMode,
            tournament.FormatTemplateId,
            tournament.Format,
            tournament.Entries
                .Select(e => new EntryDetail(
                    e.Id,
                    e.ParticipantId,
                    names.GetValueOrDefault(e.ParticipantId, "(unbekannt)"),
                    e.Seed,
                    e.Status))
                .ToList(),
            tournament.Version);
    }

    public Task UpdateAsync(
        Guid tournamentId,
        UpdateTournamentRequest request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, tournament =>
        {
            tournament.Rename(request.Name);
            tournament.Reschedule(request.StartsOn, request.EndsOn);
        }, cancellationToken);

    // --- Zustandsübergänge ------------------------------------------------

    public Task OpenRegistrationAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.OpenRegistration(), cancellationToken);

    public Task CloseRegistrationAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.CloseRegistration(), cancellationToken);

    public async Task GenerateDrawAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);

        var template = await _templates.FindAsync(tournament.FormatTemplateId, cancellationToken)
            ?? throw new NotFoundException("Formatvorlage", tournament.FormatTemplateId);

        tournament.GenerateDraw(template.Definition, template.Version);
        await _drawBuilder.BuildAsync(tournament, cancellationToken);

        // Zwischenspeichern, damit die eben angelegten Phasen für den Aufbau der
        // öffentlichen Ansicht abfragbar sind. Endgültig wird beides erst mit
        // dem Abschluss der Einheit — und zwar gemeinsam.
        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournamentId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Nimmt die Auslosung zurück und verwirft den Draw.
    ///
    /// Beides gehört zusammen: bliebe der Baum stehen, stünden nach einer
    /// Nachmeldung Matches im System, die zu einem Teilnehmerfeld gehören, das
    /// es so nicht mehr gibt.
    /// </summary>
    public async Task ReopenRegistrationAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);

        tournament.ReopenRegistration();
        await _drawBuilder.DiscardAsync(tournamentId, cancellationToken);

        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournamentId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task StartAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.Start(), cancellationToken);

    public Task CompleteAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.Complete(), cancellationToken);

    public Task AbandonAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.Abandon(), cancellationToken);

    public Task SwitchToMatchDayAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.SwitchToMatchDay(), cancellationToken);

    public Task SwitchToPlanningAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.SwitchToPlanning(), cancellationToken);

    // --- Meldungen --------------------------------------------------------

    public async Task<Guid> EnterAsync(
        Guid tournamentId,
        EnterTournamentRequest request,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);

        _ = await _players.FindParticipantAsync(request.ParticipantId, cancellationToken)
            ?? throw new NotFoundException("Teilnehmer", request.ParticipantId);

        var entry = tournament.Enter(Guid.NewGuid(), request.ParticipantId, request.Seed);
        await _publicView.RebuildAsync(tournamentId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return entry.Id;
    }

    public Task AcceptAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.Accept(entryId), cancellationToken);

    public Task MoveToWaitingListAsync(
        Guid tournamentId,
        Guid entryId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.MoveToWaitingList(entryId), cancellationToken);

    public Task WithdrawAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.Withdraw(entryId), cancellationToken);

    public Task SetSeedAsync(
        Guid tournamentId,
        Guid entryId,
        SetSeedRequest request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.SetSeed(entryId, request.Seed), cancellationToken);

    // --- Innere Helfer ----------------------------------------------------

    /// <summary>
    /// Lädt, prüft die Berechtigung, wendet die Änderung an und speichert.
    ///
    /// Die Berechtigung wird <em>nach</em> dem Laden geprüft, damit ein Turnier
    /// außerhalb des Scopes als 404 endet und nicht als 403 — ein 403 verriete
    /// seine Existenz (ADR-0004).
    /// </summary>
    private async Task MutateAsync(
        Guid tournamentId,
        Action<Tournament> change,
        CancellationToken cancellationToken)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);
        change(tournament);
        // Jede Änderung am Turnier kann die öffentliche Ansicht betreffen — der
        // Name, die Termine, der Zustand, eine Setzposition. Statt zu erraten,
        // welche es tut, wird immer neu gebaut; ob dabei etwas herauskommt,
        // entscheidet der Vergleich in der Projektion (ADR-0003). Beides geht in
        // einem Zug in die Datenbank, sonst können sie auseinanderlaufen.
        await _publicView.RebuildAsync(tournamentId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Tournament> Load(Guid tournamentId, CancellationToken cancellationToken) =>
        await _tournaments.FindAsync(tournamentId, cancellationToken)
        ?? throw new NotFoundException("Turnier", tournamentId);

    private async Task<Tournament> LoadForManagement(Guid tournamentId, CancellationToken cancellationToken)
    {
        var tournament = await Load(tournamentId, cancellationToken);

        // Beide Wege sind zulässig: Turnierleiter des Turniers oder
        // Administrator des ausrichtenden Vereins.
        User.Require(
            Permission.ManageTournament,
            ResourceScope.Tournament(tournamentId),
            ResourceScope.Club(tournament.ClubId));

        return tournament;
    }

    private async Task<Dictionary<Guid, string>> ParticipantNamesAsync(
        Tournament tournament,
        CancellationToken cancellationToken)
    {
        var ids = tournament.Entries.Select(e => e.ParticipantId).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var participants = await _players.FindParticipantsAsync(ids, cancellationToken);
        return participants.ToDictionary(p => p.Id, p => p.DisplayName);
    }
}
