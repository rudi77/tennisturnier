using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Adapters.Scheduling;

/// <summary>
/// Ein erklärbarer Spielplan-Solver: Prioritätenliste nach kritischer Pfadtiefe,
/// danach jeweils der früheste zulässige Platz.
///
/// Bewusst kein Optimierungsverfahren. Für ein Vereinsturnier — bis etwa 128
/// Teilnehmer auf höchstens einem Dutzend Plätzen — reicht das Ergebnis, und es
/// hat eine Eigenschaft, die ein Optimum nicht hätte: zu jeder Ansetzung lässt
/// sich in einem Satz sagen, warum sie dort liegt. Genau daran entscheidet sich,
/// ob die Turnierleitung das Werkzeug benutzt oder umgeht (ADR-0002).
///
/// Die kritische Pfadtiefe zuerst, weil ein Match, an dem eine lange Kette
/// hängt, den ganzen Plan bestimmt: das Viertelfinale zu spät angesetzt,
/// und Halbfinale und Finale rutschen mit.
/// </summary>
public sealed class HeuristicScheduleSolver : IScheduleSolver
{
    /// <summary>
    /// Wechselpause zwischen zwei Matches auf demselben Platz — abgehen,
    /// abziehen, aufwärmen.
    /// </summary>
    private static readonly TimeSpan CourtTurnaround = TimeSpan.FromMinutes(10);

    public ScheduleProposal Solve(SchedulingProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        if (problem.Courts.Count == 0)
        {
            return new ScheduleProposal(
                [],
                [.. problem.Matches.Select(match => new UnscheduledMatch(
                    match.Id, "Der Verein hat keinen bespielbaren Platz."))],
                [],
                new ScheduleDiff(0, 0, 0, 0));
        }

        var state = new PlacementState(problem);

        // Von Hand gesetzte und festgenagelte Zuweisungen sind harte Vorgaben:
        // die Turnierleitung kennt Umstände, die das System nicht kennt. Sie
        // belegen ihren Platz, bevor irgendetwas gerechnet wird.
        foreach (var fixedAssignment in problem.Existing.Where(a => a.IsFixedForSolver && a.PlannedSlot is not null))
        {
            state.KeepFixed(fixedAssignment);
        }

        var ordered = Prioritise(problem);

        // Erst behalten, dann neu legen — und zwar in zwei getrennten Durchgängen.
        // Zusammen in einem gingen die Ansetzungen verloren, die eigentlich hätten
        // bleiben können: ein Match, das neu gelegt werden muss, nähme dabei den
        // frühesten freien Platz und damit den Termin eines späteren, das ihn noch
        // hatte — und das löste seinerseits das nächste aus. Aus einer
        // Verschiebung von Hand würde so ein neuer Aushang (ADR-0002).
        foreach (var match in ordered.Where(match => !state.IsPlaced(match.Id)))
        {
            state.TryKeepPrevious(match);
        }

        foreach (var match in ordered.Where(match => !state.IsPlaced(match.Id)))
        {
            state.Place(match);
        }

        return state.ToProposal();
    }

    /// <summary>
    /// Die Reihenfolge, in der Matches angesetzt werden: erst die mit der
    /// längsten Kette hinter sich, bei Gleichstand die frühere Runde.
    /// </summary>
    private static IReadOnlyList<Match> Prioritise(SchedulingProblem problem)
    {
        var depth = CriticalDepth(problem.Matches);

        return
        [
            .. problem.Matches
                .OrderByDescending(match => depth[match.Id])
                .ThenBy(match => match.Round)
                .ThenBy(match => match.Position),
        ];
    }

    /// <summary>
    /// Wie viele Matches hinter einem Match noch hängen — die Länge der längsten
    /// Folgekette.
    /// </summary>
    private static Dictionary<Guid, int> CriticalDepth(IReadOnlyList<Match> matches)
    {
        var successors = new Dictionary<Guid, List<Guid>>();

        foreach (var match in matches)
        {
            foreach (var predecessor in PredecessorsOf(match))
            {
                if (!successors.TryGetValue(predecessor, out var list))
                {
                    successors[predecessor] = list = [];
                }

                list.Add(match.Id);
            }
        }

        var depth = new Dictionary<Guid, int>();

        foreach (var match in matches)
        {
            Depth(match.Id, successors, depth, guard: matches.Count);
        }

        return depth;
    }

    private static int Depth(
        Guid matchId,
        Dictionary<Guid, List<Guid>> successors,
        Dictionary<Guid, int> depth,
        int guard)
    {
        if (depth.TryGetValue(matchId, out var known))
        {
            return known;
        }

        if (guard < 0)
        {
            throw new DomainException("Die Abhängigkeiten der Matches enthalten einen Zyklus.");
        }

        var value = successors.TryGetValue(matchId, out var next)
            ? next.Max(successor => Depth(successor, successors, depth, guard - 1)) + 1
            : 0;

        depth[matchId] = value;

        return value;
    }

    private static IEnumerable<Guid> PredecessorsOf(Match match)
    {
        if (match.Side1.Origin.DependsOnMatch is { } first)
        {
            yield return first;
        }

        if (match.Side2.Origin.DependsOnMatch is { } second)
        {
            yield return second;
        }
    }

    /// <summary>
    /// Der Stand des Legens: was schon liegt, wann welcher Spieler zuletzt vom
    /// Platz ging, und was daraus wird.
    /// </summary>
    private sealed class PlacementState
    {
        private readonly SchedulingProblem _problem;
        private readonly Dictionary<Guid, List<TimeSlot>> _occupiedByCourt = [];
        private readonly Dictionary<Guid, List<TimeSlot>> _playedByPlayer = [];
        private readonly Dictionary<Guid, TimeSlot> _slotByMatch = [];
        private readonly List<ProposedAssignment> _assignments = [];
        private readonly List<UnscheduledMatch> _unscheduled = [];

        public PlacementState(SchedulingProblem problem)
        {
            _problem = problem;

            foreach (var court in problem.Courts)
            {
                _occupiedByCourt[court.Id] = [];
            }
        }

        public bool IsPlaced(Guid matchId) => _slotByMatch.ContainsKey(matchId);

        public void KeepFixed(CourtAssignment assignment)
        {
            var slot = assignment.PlannedSlot!.Value;
            var court = _problem.Courts.FirstOrDefault(c => c.Id == assignment.CourtId);

            Occupy(assignment.MatchId, assignment.CourtId, slot);

            _assignments.Add(new ProposedAssignment(
                assignment.MatchId,
                assignment.CourtId,
                court?.Name ?? "(unbekannt)",
                assignment.SequenceOnCourt,
                slot.Start,
                assignment.EstimatedDuration,
                ProposalChange.Unchanged,
                assignment.Source == AssignmentSource.Pinned
                    ? "Festgenagelt — der Solver ändert daran nichts."
                    : "Von Hand gesetzt und deshalb unangetastet."));
        }

        public void Place(Match match)
        {
            var duration = _problem.DurationOf(match.Id);
            var readyAt = ReadyAt(match);

            var previousCourt = _problem.Existing.FirstOrDefault(a => a.MatchId == match.Id)?.CourtId;

            var best = _problem.Courts
                .Select(court => (Court: court, Start: EarliestStart(court, match, readyAt, duration)))
                .Where(candidate => candidate.Start is not null)
                .OrderBy(candidate => candidate.Start!.Value)
                // Bei gleicher Zeit zuerst der bisherige Platz. Ein Match, das
                // ohnehin eine halbe Stunde später beginnt, muss nicht auch noch
                // den Platz wechseln — das wäre eine Änderung im Aushang ohne
                // jeden Gewinn.
                .ThenByDescending(candidate => candidate.Court.Id == previousCourt)
                // Danach das Finale auf den Center Court, alles andere davon weg.
                // Ein weicher Wunsch, der nur zwischen sonst gleichwertigen
                // Möglichkeiten entscheidet.
                .ThenByDescending(candidate => candidate.Court.IsCenterCourt == IsShowcase(match))
                .ThenBy(candidate => candidate.Court.Name, StringComparer.CurrentCulture)
                .FirstOrDefault();

            if (best.Court is null || best.Start is not { } start)
            {
                _unscheduled.Add(new UnscheduledMatch(
                    match.Id,
                    "Kein Platz hat ein freies Fenster, das lang genug ist und die Pausen einhält."));
                return;
            }

            var slot = new TimeSlot(start, start + duration);
            Occupy(match.Id, best.Court.Id, slot);

            var previous = _problem.Existing.FirstOrDefault(a => a.MatchId == match.Id);

            _assignments.Add(new ProposedAssignment(
                match.Id,
                best.Court.Id,
                best.Court.Name,
                _occupiedByCourt[best.Court.Id].Count,
                start,
                duration,
                ChangeAgainst(previous, best.Court.Id, start),
                Explain(match, readyAt, start)));
        }

        /// <summary>
        /// Behält die bestehende Ansetzung, wenn sie noch zulässig ist.
        ///
        /// Das ist der Stabilitätsterm, und er ist der eigentliche Knackpunkt aus
        /// ADR-0002: ein Solver, der nach jeder kleinen Änderung den halben Plan
        /// neu legt, wird nach dem ersten Turniertag abgeschaltet — der Aushang
        /// hängt längst, und die Spieler haben ihre Zeiten notiert. Neu gerechnet
        /// wird nur, was durch die Änderung tatsächlich unmöglich geworden ist.
        /// </summary>
        public bool TryKeepPrevious(Match match)
        {
            var previous = _problem.Existing.FirstOrDefault(a =>
                a.MatchId == match.Id && a.Status == AssignmentStatus.Planned);

            if (previous?.PlannedStart is not { } start)
            {
                return false;
            }

            // Nur behalten, wenn auch alle Vorgänger geblieben sind. Ein Match,
            // dessen Vorspiel neu gelegt werden muss, kann seinen alten Termin
            // nicht halten — sonst begänne es womöglich, bevor feststeht, wer
            // darin spielt.
            var duration = _problem.DurationOf(match.Id);
            var slot = new TimeSlot(start, start + duration);
            var court = _problem.Courts.FirstOrDefault(c => c.Id == previous.CourtId);

            foreach (var predecessorId in PredecessorsOf(match))
            {
                if (!_slotByMatch.TryGetValue(predecessorId, out var predecessor)
                    || start < predecessor.End + _problem.MinimumRest)
                {
                    return false;
                }
            }

            var fits = court is not null
                && court.FreeWindows.Any(window => window.Start <= slot.Start && slot.End <= window.End)
                && !_occupiedByCourt[court.Id].Any(occupied => occupied.Overlaps(slot))
                && !ConflictsWithPlayers(match, slot);

            if (!fits)
            {
                return false;
            }

            Occupy(match.Id, court!.Id, slot);

            _assignments.Add(new ProposedAssignment(
                match.Id,
                court.Id,
                court.Name,
                previous.SequenceOnCourt,
                start,
                duration,
                ProposalChange.Unchanged,
                "Unverändert gegenüber dem bestehenden Plan."));

            return true;
        }

        /// <summary>
        /// Ab wann das Match frühestens beginnen kann: nach dem Ende aller
        /// Vorgänger und nach der Pause seiner Spieler.
        /// </summary>
        private DateTimeOffset ReadyAt(Match match)
        {
            var earliest = _problem.Courts
                .SelectMany(court => court.FreeWindows)
                .Select(window => window.Start)
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Min();

            foreach (var predecessorId in PredecessorsOf(match))
            {
                if (_slotByMatch.TryGetValue(predecessorId, out var predecessor))
                {
                    earliest = Later(earliest, predecessor.End + _problem.MinimumRest);
                }
            }

            foreach (var playerId in PlayersOf(match))
            {
                if (_playedByPlayer.TryGetValue(playerId, out var played) && played.Count > 0)
                {
                    earliest = Later(earliest, played.Max(slot => slot.End) + _problem.MinimumRest);
                }
            }

            return earliest;
        }

        /// <summary>
        /// Der früheste Beginn auf diesem Platz, der in ein freies Fenster passt
        /// und keine schon liegende Ansetzung berührt.
        /// </summary>
        private DateTimeOffset? EarliestStart(
            SchedulableCourt court,
            Match match,
            DateTimeOffset readyAt,
            TimeSpan duration)
        {
            foreach (var window in court.FreeWindows.OrderBy(w => w.Start))
            {
                var start = Later(window.Start, readyAt);

                // Innerhalb des Fensters nach vorne rücken, bis nichts mehr im
                // Weg steht. Jede belegte Zeit schiebt den Beginn hinter ihr
                // Ende plus Wechselpause.
                while (start + duration <= window.End)
                {
                    var slot = new TimeSlot(start, start + duration);
                    var blocking = _occupiedByCourt[court.Id]
                        .Where(occupied => occupied.Overlaps(slot))
                        .Select(occupied => occupied.End)
                        .DefaultIfEmpty(DateTimeOffset.MinValue)
                        .Max();

                    if (blocking == DateTimeOffset.MinValue && !ConflictsWithPlayers(match, slot))
                    {
                        return start;
                    }

                    start = blocking == DateTimeOffset.MinValue
                        ? NextPlayerFreeStart(match, slot)
                        : blocking + CourtTurnaround;
                }
            }

            return null;
        }

        /// <summary>
        /// Spielt hier jemand, der zu dieser Zeit schon auf einem anderen Platz
        /// steht? Der Bezug ist der Spieler und nicht die Meldung — wer Einzel
        /// und Doppel spielt, steckt in zwei Meldungen.
        /// </summary>
        private bool ConflictsWithPlayers(Match match, TimeSlot slot) =>
            PlayersOf(match).Any(playerId =>
                _playedByPlayer.TryGetValue(playerId, out var played)
                && played.Any(other => other.Overlaps(slot)
                    || (slot.Start - other.End >= TimeSpan.Zero && slot.Start - other.End < _problem.MinimumRest)
                    || (other.Start - slot.End >= TimeSpan.Zero && other.Start - slot.End < _problem.MinimumRest)));

        private DateTimeOffset NextPlayerFreeStart(Match match, TimeSlot slot) =>
            PlayersOf(match)
                .Where(playerId => _playedByPlayer.ContainsKey(playerId))
                .SelectMany(playerId => _playedByPlayer[playerId])
                .Where(other => other.End + _problem.MinimumRest > slot.Start)
                .Select(other => other.End + _problem.MinimumRest)
                .DefaultIfEmpty(slot.Start + TimeSpan.FromMinutes(5))
                .Min();

        private void Occupy(Guid matchId, Guid courtId, TimeSlot slot)
        {
            _slotByMatch[matchId] = slot;

            if (!_occupiedByCourt.TryGetValue(courtId, out var occupied))
            {
                _occupiedByCourt[courtId] = occupied = [];
            }

            occupied.Add(slot);

            var match = _problem.Matches.FirstOrDefault(m => m.Id == matchId);
            if (match is null)
            {
                return;
            }

            foreach (var playerId in PlayersOf(match))
            {
                if (!_playedByPlayer.TryGetValue(playerId, out var played))
                {
                    _playedByPlayer[playerId] = played = [];
                }

                played.Add(slot);
            }
        }

        private IEnumerable<Guid> PlayersOf(Match match)
        {
            foreach (var entryId in new[] { match.Side1.EntryId, match.Side2.EntryId })
            {
                if (entryId is { } id && _problem.PlayersByEntry.TryGetValue(id, out var players))
                {
                    foreach (var playerId in players)
                    {
                        yield return playerId;
                    }
                }
            }
        }

        /// <summary>
        /// Das Finale und das Spiel um Platz 3 gehören auf den Center Court, alles
        /// andere möglichst nicht.
        /// </summary>
        private bool IsShowcase(Match match) =>
            _problem.Matches.Count > 0 && match.Round == _problem.Matches.Max(m => m.Round);

        private static ProposalChange ChangeAgainst(
            CourtAssignment? previous,
            Guid courtId,
            DateTimeOffset start) =>
            previous is null ? ProposalChange.Added
            : previous.CourtId == courtId && previous.PlannedStart == start ? ProposalChange.Unchanged
            : ProposalChange.Moved;

        /// <summary>
        /// Warum das Match dort liegt: der Constraint, der es bindet. Liegt es
        /// später als der erste freie Termin, ist es ein Vorgänger oder eine
        /// Pause; sonst ist es schlicht der früheste Termin.
        /// </summary>
        private string Explain(Match match, DateTimeOffset readyAt, DateTimeOffset start)
        {
            var waitsFor = PredecessorsOf(match)
                .Where(_slotByMatch.ContainsKey)
                .Select(id => _slotByMatch[id])
                .OrderByDescending(slot => slot.End)
                .FirstOrDefault();

            if (waitsFor != default && waitsFor.End + _problem.MinimumRest >= readyAt)
            {
                return $"Frühestmöglich nach dem Vorspiel, das um {waitsFor.End:HH:mm} endet, " +
                       $"zuzüglich {_problem.MinimumRest.TotalMinutes:0} Minuten Pause.";
            }

            return start > readyAt
                ? "Erster Zeitpunkt, zu dem ein Platz frei ist."
                : "Frühestmöglicher Beginn.";
        }

        public ScheduleProposal ToProposal()
        {
            var ordered = _assignments
                .OrderBy(assignment => assignment.PlannedStart)
                .ThenBy(assignment => assignment.CourtName, StringComparer.CurrentCulture)
                .ToList();

            // Die Position in der Warteschlange folgt der Zeit — auf jedem Platz
            // beginnend bei 1, damit sie am Turniertag ohne Umrechnung stimmt.
            var sequences = new Dictionary<Guid, int>();
            var numbered = ordered
                .Select(assignment => assignment with
                {
                    SequenceOnCourt = sequences[assignment.CourtId] =
                        sequences.GetValueOrDefault(assignment.CourtId) + 1,
                })
                .ToList();

            var diff = new ScheduleDiff(
                Unchanged: numbered.Count(a => a.Change == ProposalChange.Unchanged),
                Added: numbered.Count(a => a.Change == ProposalChange.Added),
                Moved: numbered.Count(a => a.Change == ProposalChange.Moved),
                Removed: _problem.Existing.Count(existing =>
                    numbered.All(assignment => assignment.MatchId != existing.MatchId)));

            return new ScheduleProposal(numbered, _unscheduled, [], diff);
        }

        private static DateTimeOffset Later(DateTimeOffset left, DateTimeOffset right) =>
            left > right ? left : right;
    }
}
