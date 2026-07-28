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
        var plan = await LoadAsync(tournamentId, cancellationToken);

        return Describe(_solver.Solve(plan.Problem), plan);
    }

    public async Task<SchedulePlanResult> ConfirmAsync(
        Guid tournamentId,
        ConfirmScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireWellFormed(request);

        var plan = await LoadAsync(tournamentId, cancellationToken);
        var applied = Apply(plan, request);

        // Der Zähler des Turniers ist die Klammer um den ganzen Plan. Ohne ihn
        // liefen zwei gleichzeitige Bestätigungen beide durch und legten jedes
        // Match zweimal an — auf einer noch nicht existierenden Zeile wirkt kein
        // Zähler.
        plan.Tournament.MarkScheduleChanged();

        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(plan.Tournament.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Geprüft wird der ganze Plan, nicht nur das eben Übernommene: eine
        // Kollision entsteht zwischen zwei Ansetzungen, und die andere kann
        // längst gespeichert sein.
        var violations = new ScheduleValidator().Validate(
            [.. applied.Union(Untouched(plan, applied))], plan.Problem.ToContext());

        return new SchedulePlanResult(
            [.. applied.Select(assignment => Describe(assignment, plan))],
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
    private IReadOnlyList<CourtAssignment> Apply(Plan plan, ConfirmScheduleRequest request)
    {
        var byMatch = plan.Problem.Existing
            .Where(assignment => !assignment.IsOver)
            .GroupBy(assignment => assignment.MatchId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var applied = new List<CourtAssignment>();

        foreach (var confirmed in request.Assignments)
        {
            applied.Add(ApplyOne(plan, confirmed, byMatch.GetValueOrDefault(confirmed.MatchId, [])));
        }

        RemoveOrphans(plan, byMatch);

        return applied;
    }

    private CourtAssignment ApplyOne(
        Plan plan,
        ConfirmedAssignment confirmed,
        IReadOnlyList<CourtAssignment> active)
    {
        var match = plan.Problem.Matches.FirstOrDefault(m => m.Id == confirmed.MatchId)
            ?? throw MissingMatch(plan, confirmed.MatchId);

        var court = plan.Club.Courts.FirstOrDefault(c => c.Id == confirmed.CourtId && c.IsActive)
            ?? throw new NotFoundException("Platz", confirmed.CourtId);

        RequireWithinTournament(plan.Tournament, confirmed);

        // Ein Match, das schon aufgerufen wurde oder läuft, wird nicht mehr
        // geplant. Eine zweite, parallele Ansetzung daneben zu legen hieße, es
        // gleichzeitig auf zwei Plätzen anzusetzen.
        var running = active.FirstOrDefault(assignment => assignment.Status != AssignmentStatus.Planned);
        if (running is not null)
        {
            throw new DomainException(
                $"„{plan.Labels.GetValueOrDefault(match.Id) ?? "Das Match"}“ ist bereits {running.Status} " +
                "und lässt sich nicht mehr einplanen.");
        }

        if (active.FirstOrDefault() is { } existing)
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

            return existing;
        }

        var created = new CourtAssignment(
            Guid.NewGuid(),
            plan.Tournament.Id,
            match.Id,
            court.Id,
            confirmed.SequenceOnCourt,
            confirmed.EstimatedDuration,
            AssignmentSource.Auto);

        created.PlanFor(confirmed.PlannedStart);
        _assignments.Add(created);

        return created;
    }

    /// <summary>
    /// Räumt Ansetzungen ab, deren Match inzwischen gespielt ist.
    ///
    /// Ohne das bliebe die Zeit des gespielten Matches für den Rest des Turniers
    /// belegt — in der öffentlichen Warteschlange sichtbar und, schlimmer, als
    /// stiller Kollisionspartner für alles, was danach dorthin geplant wird.
    /// Ansetzungen, die schlicht nicht in der Bestätigung stehen, bleiben
    /// unangetastet: eine Teilbestätigung ist keine Aufforderung, den Rest zu
    /// löschen.
    /// </summary>
    private void RemoveOrphans(Plan plan, IReadOnlyDictionary<Guid, List<CourtAssignment>> byMatch)
    {
        foreach (var (matchId, assignments) in byMatch)
        {
            if (plan.Problem.Matches.Any(match => match.Id == matchId))
            {
                continue;
            }

            foreach (var orphan in assignments.Where(a => a.Status == AssignmentStatus.Planned))
            {
                _assignments.Remove(orphan);
            }
        }
    }

    /// <summary>Die noch gespeicherten Ansetzungen, die diese Bestätigung nicht berührt.</summary>
    private static IEnumerable<CourtAssignment> Untouched(Plan plan, IReadOnlyList<CourtAssignment> applied) =>
        plan.Problem.Existing.Where(existing =>
            !existing.IsOver
            && applied.All(assignment => assignment.Id != existing.Id)
            && plan.Problem.Matches.Any(match => match.Id == existing.MatchId));

    /// <summary>
    /// Ein Match, das es im Turnier gibt, aber nicht mehr anzusetzen ist, ist
    /// inzwischen gespielt: der Vorschlag ist überholt. Das ist ein Konflikt und
    /// kein „nicht gefunden" — der Aufrufer soll neu rechnen, nicht suchen.
    /// </summary>
    private static Exception MissingMatch(Plan plan, Guid matchId) =>
        plan.AllMatchIds.Contains(matchId)
            ? new ConcurrencyConflictException(new DomainException(
                $"Das Match {matchId} ist nicht mehr zu planen — es ist entschieden oder bereits am Platz."))
            : new NotFoundException("Match", matchId);

    private static void RequireWellFormed(ConfirmScheduleRequest request)
    {
        if (request.Assignments is null)
        {
            throw new DomainException("Die Bestätigung nennt keine Ansetzungen.");
        }

        var duplicate = request.Assignments
            .GroupBy(assignment => assignment.MatchId)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new DomainException(
                $"Das Match {duplicate.Key} kommt in der Bestätigung mehrfach vor. " +
                "Ein Match steht zu einer Zeit auf genau einem Platz.");
        }
    }

    /// <summary>
    /// Der geplante Beginn muss im Turnierzeitraum liegen — mit einem Tag Luft
    /// nach jeder Seite für Zeitzonen und einen Abend, der über Mitternacht
    /// hinausgeht. Ohne diese Schranke wanderte ein vertippter Termin
    /// unbemerkt ins Jahr 2099 und stünde dort öffentlich.
    /// </summary>
    private static void RequireWithinTournament(Tournament tournament, ConfirmedAssignment confirmed)
    {
        var from = new DateTimeOffset(tournament.StartsOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var until = new DateTimeOffset(tournament.EndsOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2);

        if (confirmed.PlannedStart < from || confirmed.PlannedStart >= until)
        {
            throw new DomainException(
                $"Der Beginn {confirmed.PlannedStart:g} liegt außerhalb des Turnierzeitraums " +
                $"({tournament.StartsOn:d} bis {tournament.EndsOn:d}).");
        }
    }

    // --- Aufbau der Aufgabe ------------------------------------------------

    private async Task<Plan> LoadAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        var tournament = await _tournaments.FindAsync(tournamentId, cancellationToken)
            ?? throw new NotFoundException("Turnier", tournamentId);

        // Vor dem Laden des ganzen Spielplans: für Unbefugte soll dieses Turnier
        // nicht einmal seinen Zustand verraten — und das teure Laden hat für sie
        // ohnehin keinen Zweck (ADR-0004).
        RequireManagePermission(tournament);
        RequirePlanningMode(tournament);

        var club = await _clubs.FindAsync(tournament.ClubId, cancellationToken)
            ?? throw new NotFoundException("Verein", tournament.ClubId);

        var definition = tournament.Format?.Definition
            ?? throw new DomainException("Ein Spielplan setzt eine Auslosung voraus.");

        var phases = await _phases.ListByTournamentAsync(tournamentId, cancellationToken);
        var existing = await _assignments.ListByTournamentAsync(tournamentId, cancellationToken);

        // Beendete Matches brauchen keinen Platz mehr. Sie mitzuplanen hieße,
        // Zeit für etwas zu reservieren, das schon gespielt ist.
        //
        // Und was am Platz bereits aufgerufen wurde, läuft oder unterbrochen ist,
        // gehört dem Tagesbetrieb. Es anzubieten hieße, einen Vorschlag zu
        // liefern, den die Bestätigung anschließend geschlossen ablehnt.
        var atCourt = existing
            .Where(a => a.Status is AssignmentStatus.Called or AssignmentStatus.Running
                or AssignmentStatus.Suspended)
            .Select(a => a.MatchId)
            .ToHashSet();

        var matches = phases
            .SelectMany(phase => phase.Matches.Select(match => (Phase: phase, Match: match)))
            .Where(pair => pair.Match.Status != MatchStatus.Finished && !atCourt.Contains(pair.Match.Id))
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

        var all = phases.SelectMany(phase => phase.Matches).ToList();

        return new Plan(
            tournament,
            club,
            problem,
            all.ToDictionary(match => match.Id, match => match.Label),
            [.. all.Select(match => match.Id)]);
    }

    /// <summary>
    /// Alles, was ein Spielplanlauf braucht — samt der Matches, die nicht mehr
    /// angesetzt werden. Ohne sie ließe sich ein bereits gespieltes Match nicht
    /// von einem fremden unterscheiden.
    /// </summary>
    private sealed record Plan(
        Tournament Tournament,
        Club Club,
        SchedulingProblem Problem,
        IReadOnlyDictionary<Guid, string?> Labels,
        IReadOnlySet<Guid> AllMatchIds)
    {
        public Plan(
            Tournament tournament,
            Club club,
            SchedulingProblem problem,
            IReadOnlyDictionary<Guid, string?> labels,
            IReadOnlyList<Guid> allMatchIds)
            : this(tournament, club, problem, labels, allMatchIds.ToHashSet())
        {
        }
    }

    private static IEnumerable<SchedulableCourt> Courts(Club club, Tournament tournament)
    {
        var calendar = new CourtCalendar(club.TimeZone);
        var range = calendar.TournamentRange(tournament.StartsOn, tournament.EndsOn);

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

    private static SchedulePlanResult Describe(ScheduleProposal proposal, Plan plan)
    {
        var problem = plan.Problem;
        var labels = plan.Labels;

        // Der Vorschlag wird gegen denselben Prüfer gehalten wie ein Plan von
        // Hand. Ein Solver, der seine eigenen Ergebnisse für zulässig erklärt,
        // prüft nichts.
        //
        // Geprüft wird dabei der ganze Plan: die gespeicherten Ansetzungen, die
        // im Vorschlag nicht vorkommen, gehören dazu. Eine Kollision entsteht
        // zwischen zwei Ansetzungen, und ein Prüfer, der nur die halbe Eingabe
        // sieht, findet sie nie.
        var proposed = proposal.Assignments.Select(assignment => Materialise(assignment, problem)).ToList();

        var candidates = proposed
            .Concat(problem.Existing.Where(existing =>
                !existing.IsOver
                && proposed.All(assignment => assignment.MatchId != existing.MatchId)
                && problem.Matches.Any(match => match.Id == existing.MatchId)))
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

    private static ProposedAssignmentDetail Describe(CourtAssignment assignment, Plan plan) => new(
            assignment.MatchId,
            plan.Labels.GetValueOrDefault(assignment.MatchId),
            assignment.CourtId,
            plan.Problem.Courts.FirstOrDefault(court => court.Id == assignment.CourtId)?.Name ?? "(unbekannt)",
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
