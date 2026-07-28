using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

/// <summary>Spielplanvorschlag und seine Bestätigung (ADR-0002).</summary>
public interface ISchedulingService
{
    /// <summary>
    /// Rechnet einen Vorschlag, ohne etwas zu verändern.
    ///
    /// Das Trennen von Rechnen und Übernehmen ist der Kern: ein Solverlauf, der
    /// den Plan still überschreibt, ist genau das, was Turnierleitungen dazu
    /// bringt, die Automatik abzuschalten.
    /// </summary>
    Task<SchedulePlanResult> ProposeAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>Übernimmt einen zuvor gerechneten Vorschlag.</summary>
    Task<SchedulePlanResult> ConfirmAsync(
        Guid tournamentId,
        ConfirmScheduleRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Ein Vorschlag mit allem, was die Turnierleitung zur Entscheidung braucht.</summary>
public sealed record SchedulePlanResult(
    IReadOnlyList<ProposedAssignmentDetail> Assignments,
    IReadOnlyList<UnscheduledMatchDetail> Unscheduled,
    IReadOnlyList<ScheduleViolationDetail> Violations,
    ScheduleDiffDetail Diff);

public sealed record ProposedAssignmentDetail(
    Guid MatchId,
    string? Label,
    Guid CourtId,
    string CourtName,
    int SequenceOnCourt,
    DateTimeOffset PlannedStart,
    DateTimeOffset PlannedEnd,
    TimeSpan EstimatedDuration,
    ProposalChange Change,
    string Reason);

public sealed record UnscheduledMatchDetail(Guid MatchId, string? Label, string Reason);

public sealed record ScheduleDiffDetail(int Unchanged, int Added, int Moved, int Removed);

/// <summary>
/// Die Bestätigung nennt die zu übernehmenden Ansetzungen ausdrücklich.
///
/// Sie wird nicht aus dem letzten Lauf geraten: zwischen Rechnen und Bestätigen
/// kann jemand ein Ergebnis eintragen, und dann gilt der Vorschlag von vorhin
/// nicht mehr. Was hier steht, wird geprüft und übernommen — nicht mehr.
/// </summary>
public sealed record ConfirmScheduleRequest(IReadOnlyList<ConfirmedAssignment> Assignments);

public sealed record ConfirmedAssignment(
    Guid MatchId,
    Guid CourtId,
    int SequenceOnCourt,
    DateTimeOffset PlannedStart,
    TimeSpan EstimatedDuration);

public sealed class SchedulingService : ISchedulingService
{
    /// <summary>ADR-0002 nennt 30 Minuten. Ab einer eigenen Turniereinstellung kommt sie von dort.</summary>
    private static readonly TimeSpan DefaultRest = TimeSpan.FromMinutes(30);

    private readonly ITournamentRepository _tournaments;
    private readonly IPhaseRepository _phases;
    private readonly ICourtAssignmentRepository _assignments;
    private readonly IClubRepository _clubs;
    private readonly IPlayerRepository _players;
    private readonly IScheduleSolver _solver;
    private readonly IPublicViewService _publicView;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;

    public SchedulingService(
        ITournamentRepository tournaments,
        IPhaseRepository phases,
        ICourtAssignmentRepository assignments,
        IClubRepository clubs,
        IPlayerRepository players,
        IScheduleSolver solver,
        IPublicViewService publicView,
        IUnitOfWork unitOfWork,
        IUserContext userContext)
    {
        _tournaments = tournaments;
        _phases = phases;
        _assignments = assignments;
        _clubs = clubs;
        _players = players;
        _solver = solver;
        _publicView = publicView;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
    }

    public async Task<SchedulePlanResult> ProposeAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default)
    {
        var (tournament, _, problem, labels) = await LoadAsync(tournamentId, cancellationToken);

        // Auch das bloße Rechnen ist ein Werkzeug der Turnierleitung: es liest
        // den ganzen Spielplan und ist nicht billig.
        RequireManagePermission(tournament);

        return Describe(_solver.Solve(problem), problem, labels);
    }

    public async Task<SchedulePlanResult> ConfirmAsync(
        Guid tournamentId,
        ConfirmScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (tournament, club, problem, labels) = await LoadAsync(tournamentId, cancellationToken);

        RequireManagePermission(tournament);
        RequirePlanningMode(tournament);

        var applied = Apply(tournament, club, problem, request);

        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournament.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Nach dem Übernehmen erneut prüfen — gegen den Stand, der jetzt
        // tatsächlich in der Datenbank steht, nicht gegen den gerechneten.
        var violations = new ScheduleValidator().Validate(applied, problem.ToContext());

        return new SchedulePlanResult(
            [.. applied.Select(assignment => Describe(assignment, problem, labels))],
            [],
            [.. violations.Select(v => new ScheduleViolationDetail(v.Constraint, v.Message, v.AssignmentId))],
            new ScheduleDiffDetail(0, 0, applied.Count, 0));
    }

    // --- Übernahme ---------------------------------------------------------

    /// <summary>
    /// Setzt die bestätigten Ansetzungen. Bereits laufende oder beendete
    /// Zuweisungen bleiben unangetastet — sie sind Teil der Historie des
    /// Turniertags (ADR-0002).
    /// </summary>
    private IReadOnlyList<CourtAssignment> Apply(
        Tournament tournament,
        Club club,
        SchedulingProblem problem,
        ConfirmScheduleRequest request)
    {
        var byMatch = problem.Existing
            .Where(assignment => assignment.Status == AssignmentStatus.Planned)
            .ToDictionary(assignment => assignment.MatchId);

        var applied = new List<CourtAssignment>();

        foreach (var confirmed in request.Assignments)
        {
            var match = problem.Matches.FirstOrDefault(m => m.Id == confirmed.MatchId)
                ?? throw new NotFoundException("Match", confirmed.MatchId);

            var court = club.Courts.FirstOrDefault(c => c.Id == confirmed.CourtId && c.IsActive)
                ?? throw new NotFoundException("Platz", confirmed.CourtId);

            if (byMatch.TryGetValue(match.Id, out var existing))
            {
                // Von Hand gesetzte Ansetzungen bleiben von Hand gesetzt. Wer sie
                // ändern will, verschiebt sie ausdrücklich — ein bestätigter
                // Vorschlag darf sie nicht stillschweigend zu Automatik machen.
                existing.Replan(
                    court.Id,
                    confirmed.SequenceOnCourt,
                    confirmed.PlannedStart,
                    existing.EarliestStart,
                    confirmed.EstimatedDuration,
                    existing.IsFixedForSolver ? existing.Source : AssignmentSource.Auto);

                applied.Add(existing);
                continue;
            }

            var created = new CourtAssignment(
                Guid.NewGuid(),
                tournament.Id,
                match.Id,
                court.Id,
                confirmed.SequenceOnCourt,
                confirmed.EstimatedDuration,
                AssignmentSource.Auto);

            created.PlanFor(confirmed.PlannedStart);

            _assignments.Add(created);
            applied.Add(created);
        }

        return applied;
    }

    // --- Aufbau der Aufgabe ------------------------------------------------

    private async Task<(Tournament Tournament, Club Club, SchedulingProblem Problem,
        IReadOnlyDictionary<Guid, string?> Labels)> LoadAsync(
        Guid tournamentId,
        CancellationToken cancellationToken)
    {
        var tournament = await _tournaments.FindAsync(tournamentId, cancellationToken)
            ?? throw new NotFoundException("Turnier", tournamentId);

        var club = await _clubs.FindAsync(tournament.ClubId, cancellationToken)
            ?? throw new NotFoundException("Verein", tournament.ClubId);

        var definition = tournament.Format?.Definition
            ?? throw new DomainException("Ein Spielplan setzt eine Auslosung voraus.");

        var phases = await _phases.ListByTournamentAsync(tournamentId, cancellationToken);
        var existing = await _assignments.ListByTournamentAsync(tournamentId, cancellationToken);

        // Beendete Matches brauchen keinen Platz mehr. Sie mitzuplanen hieße,
        // Zeit für etwas zu reservieren, das schon gespielt ist.
        var matches = phases
            .SelectMany(phase => phase.Matches.Select(match => (Phase: phase, Match: match)))
            .Where(pair => pair.Match.Status != MatchStatus.Finished)
            .ToList();

        var durations = matches.ToDictionary(
            pair => pair.Match.Id,
            pair => MatchDuration.Estimate(
                MatchFormatOf(definition, pair.Phase),
                isFinal: IsFinal(pair.Phase, pair.Match, phases)));

        var problem = new SchedulingProblem(
            [.. matches.Select(pair => pair.Match)],
            await PlayersByEntryAsync(tournament, cancellationToken),
            [.. Courts(club, tournament)],
            durations,
            DefaultRest,
            existing);

        var labels = phases
            .SelectMany(phase => phase.Matches)
            .ToDictionary(match => match.Id, match => match.Label);

        return (tournament, club, problem, labels);
    }

    private static IEnumerable<SchedulableCourt> Courts(Club club, Tournament tournament)
    {
        var calendar = new CourtCalendar(club.TimeZone);
        var range = new TimeSlot(
            new DateTimeOffset(tournament.StartsOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(tournament.EndsOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1));

        return club.Courts
            .Where(court => court.IsActive)
            .Select(court => new SchedulableCourt(
                court.Id, court.Name, court.IsCenterCourt, calendar.FreeWindows(court, range)));
    }

    private static MatchFormat MatchFormatOf(FormatDefinition definition, Phase phase)
    {
        var phaseDefinition = definition.Phases.FirstOrDefault(p => p.Ordinal == phase.Ordinal);

        return phaseDefinition is null ? definition.MatchFormat : definition.MatchFormatOf(phaseDefinition);
    }

    /// <summary>Das Endspiel: letzte Runde der letzten Phase.</summary>
    private static bool IsFinal(Phase phase, Match match, IReadOnlyList<Phase> phases) =>
        phase.Ordinal == phases.Max(p => p.Ordinal)
        && phase.Matches.Count > 0
        && match.Round == phase.Matches.Max(m => m.Round);

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> PlayersByEntryAsync(
        Tournament tournament,
        CancellationToken cancellationToken)
    {
        var participantIds = tournament.Entries.Select(e => e.ParticipantId).Distinct().ToList();
        var participants = await _players.FindParticipantsAsync(participantIds, cancellationToken);
        var byId = participants.ToDictionary(p => p.Id);

        return tournament.Entries
            .Where(entry => byId.ContainsKey(entry.ParticipantId))
            .ToDictionary(entry => entry.Id, entry => byId[entry.ParticipantId].PlayerIds);
    }

    // --- Abbildung ---------------------------------------------------------

    private static SchedulePlanResult Describe(
        ScheduleProposal proposal,
        SchedulingProblem problem,
        IReadOnlyDictionary<Guid, string?> labels)
    {
        // Der Vorschlag wird gegen denselben Prüfer gehalten wie ein Plan von
        // Hand. Ein Solver, der seine eigenen Ergebnisse für zulässig erklärt,
        // prüft nichts.
        var candidates = proposal.Assignments
            .Select(assignment => Materialise(assignment, problem))
            .ToList();

        var violations = new ScheduleValidator().Validate(candidates, problem.ToContext());

        return new SchedulePlanResult(
            [
                .. proposal.Assignments.Select(assignment => new ProposedAssignmentDetail(
                    assignment.MatchId,
                    labels.GetValueOrDefault(assignment.MatchId),
                    assignment.CourtId,
                    assignment.CourtName,
                    assignment.SequenceOnCourt,
                    assignment.PlannedStart,
                    assignment.PlannedEnd,
                    assignment.EstimatedDuration,
                    assignment.Change,
                    assignment.Reason)),
            ],
            [
                .. proposal.Unscheduled.Select(unscheduled => new UnscheduledMatchDetail(
                    unscheduled.MatchId, labels.GetValueOrDefault(unscheduled.MatchId), unscheduled.Reason)),
            ],
            [.. violations.Select(v => new ScheduleViolationDetail(v.Constraint, v.Message, v.AssignmentId))],
            new ScheduleDiffDetail(
                proposal.Diff.Unchanged, proposal.Diff.Added, proposal.Diff.Moved, proposal.Diff.Removed));
    }

    /// <summary>
    /// Ein Vorschlag als Zuweisung, nur zur Prüfung — sie wird nicht gespeichert.
    /// Die Id ist die der bestehenden Zuweisung, damit ein Verstoß auf etwas
    /// zeigt, das die Turnierleitung wiederfindet.
    /// </summary>
    private static CourtAssignment Materialise(ProposedAssignment assignment, SchedulingProblem problem)
    {
        var existing = problem.Existing.FirstOrDefault(a => a.MatchId == assignment.MatchId);

        var candidate = new CourtAssignment(
            existing?.Id ?? Guid.NewGuid(),
            existing?.TournamentId ?? Guid.NewGuid(),
            assignment.MatchId,
            assignment.CourtId,
            assignment.SequenceOnCourt,
            assignment.EstimatedDuration,
            AssignmentSource.Auto);

        candidate.PlanFor(assignment.PlannedStart);

        return candidate;
    }

    private static ProposedAssignmentDetail Describe(
        CourtAssignment assignment,
        SchedulingProblem problem,
        IReadOnlyDictionary<Guid, string?> labels) => new(
            assignment.MatchId,
            labels.GetValueOrDefault(assignment.MatchId),
            assignment.CourtId,
            problem.Courts.FirstOrDefault(court => court.Id == assignment.CourtId)?.Name ?? "(unbekannt)",
            assignment.SequenceOnCourt,
            assignment.PlannedStart ?? default,
            (assignment.PlannedStart ?? default) + assignment.EstimatedDuration,
            assignment.EstimatedDuration,
            ProposalChange.Unchanged,
            "Übernommen.");

    // --- Berechtigungen ----------------------------------------------------

    private void RequireManagePermission(Tournament tournament) =>
        _userContext.Current.Require(
            Permission.ManageTournament,
            ResourceScope.Tournament(tournament.Id),
            ResourceScope.Club(tournament.ClubId));

    /// <summary>
    /// Ein gerechneter Plan gehört in den Planungsmodus. Am Turniertag wäre eine
    /// Startzeit eine Behauptung — dort zählt die Reihenfolge auf dem Platz
    /// (ADR-0002).
    /// </summary>
    private static void RequirePlanningMode(Tournament tournament)
    {
        if (tournament.SchedulingMode != SchedulingMode.Planning)
        {
            throw new DomainException(
                "Im Turniertagbetrieb wird nicht mehr geplant, sondern aufgerufen. " +
                "Für einen gerechneten Spielplan zuerst in den Planungsmodus zurückwechseln.");
        }
    }
}
