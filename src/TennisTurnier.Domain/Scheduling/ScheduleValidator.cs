using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Scheduling;

/// <summary>Ein verletzter harter Constraint.</summary>
public sealed record ScheduleViolation(ScheduleConstraint Constraint, string Message, Guid AssignmentId);

public enum ScheduleConstraint
{
    /// <summary>Ein Spieler kann nicht zwei Matches gleichzeitig spielen.</summary>
    PlayerDoubleBooked,

    /// <summary>Mindestpause zwischen zwei Matches desselben Spielers.</summary>
    PlayerRestPeriod,

    /// <summary>Zwei Matches gleichzeitig auf demselben Platz.</summary>
    CourtDoubleBooked,

    /// <summary>Der Platz ist zu dieser Zeit nicht verfügbar.</summary>
    CourtUnavailable,

    /// <summary>Ein Match kann nicht vor dem Ende seines Vorgängers beginnen.</summary>
    DependencyOrder,
}

/// <summary>Alles, was zur Prüfung eines Spielplans gebraucht wird.</summary>
public sealed record SchedulingContext(
    IReadOnlyList<Match> Matches,
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> PlayersByEntry,
    IReadOnlyDictionary<Guid, IReadOnlyList<TimeSlot>> FreeWindowsByCourt,
    TimeSpan MinimumRestBetweenMatches);

/// <summary>
/// Prüft einen Spielplan gegen die harten Constraints aus ADR-0002.
///
/// Bewusst getrennt vom Solver: derselbe Prüfer beurteilt einen Solver-Vorschlag
/// und eine Verschiebung von Hand. Läge die Prüfung im Solver, könnte die
/// Turnierleitung von Hand einen Plan erzeugen, den der Solver nie vorgeschlagen
/// hätte — und niemand merkte es bis zum Turniertag.
///
/// Ausdrücklich <em>nicht</em> geprüft wird, ob die Teilnehmer eines Matches
/// schon feststehen. Im Planungsmodus steht der ganze Baum, bevor ein Ball
/// gespielt ist — ein Halbfinale ohne bekannte Teilnehmer einzuplanen ist der
/// Normalfall und kein Fehler. Erst beim Aufrufen am Turniertag muss klar sein,
/// wer antritt; das setzt der Tagesbetrieb durch, nicht dieser Prüfer.
/// </summary>
public sealed class ScheduleValidator
{
    /// <summary>
    /// Alle Verstöße eines Plans. Gibt bewusst eine Liste zurück und wirft nicht
    /// beim ersten Fund: wer einen Spielplan von Hand baut, will alle Probleme
    /// auf einmal sehen, nicht eines nach dem anderen.
    /// </summary>
    public IReadOnlyList<ScheduleViolation> Validate(
        IReadOnlyList<CourtAssignment> assignments,
        SchedulingContext context)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(context);

        var violations = new List<ScheduleViolation>();
        var planned = assignments.Where(a => a.PlannedSlot is not null).ToList();

        foreach (var assignment in planned)
        {
            CheckCourtAvailability(assignment, context, violations);
            CheckDependencies(assignment, planned, context, violations);
        }

        CheckCourtCollisions(planned, violations);
        CheckPlayerCollisions(planned, context, violations);

        return violations;
    }

    private static void CheckCourtAvailability(
        CourtAssignment assignment,
        SchedulingContext context,
        List<ScheduleViolation> violations)
    {
        var slot = assignment.PlannedSlot!.Value;

        if (!context.FreeWindowsByCourt.TryGetValue(assignment.CourtId, out var windows))
        {
            violations.Add(new ScheduleViolation(
                ScheduleConstraint.CourtUnavailable,
                "Für diesen Platz sind keine freien Fenster bekannt.",
                assignment.Id));
            return;
        }

        // Das Match muss vollständig in ein Fenster passen. Über zwei Fenster
        // hinweg zu planen hieße, über eine Sperre hinweg zu spielen.
        if (!windows.Any(window => window.Start <= slot.Start && slot.End <= window.End))
        {
            violations.Add(new ScheduleViolation(
                ScheduleConstraint.CourtUnavailable,
                $"Der Platz ist im Fenster {slot} nicht durchgehend verfügbar.",
                assignment.Id));
        }
    }

    private static void CheckCourtCollisions(
        IReadOnlyList<CourtAssignment> planned,
        List<ScheduleViolation> violations)
    {
        foreach (var group in planned.GroupBy(a => a.CourtId))
        {
            var ordered = group.OrderBy(a => a.PlannedSlot!.Value.Start).ToList();

            for (var index = 1; index < ordered.Count; index++)
            {
                if (ordered[index - 1].PlannedSlot!.Value.Overlaps(ordered[index].PlannedSlot!.Value))
                {
                    violations.Add(new ScheduleViolation(
                        ScheduleConstraint.CourtDoubleBooked,
                        $"Auf diesem Platz überschneidet sich die Zeit mit einem anderen Match " +
                        $"({ordered[index - 1].PlannedSlot!.Value}).",
                        ordered[index].Id));
                }
            }
        }
    }

    /// <summary>
    /// Ein Spieler kann nicht zweimal gleichzeitig spielen — und braucht zwischen
    /// zwei Matches eine Pause.
    ///
    /// Der Bezug ist der Spieler, nicht der Teilnehmer: wer im Einzel und im
    /// Doppel gemeldet ist, steht in zwei verschiedenen Teilnehmern und wäre über
    /// die Meldung allein nicht zu erkennen.
    /// </summary>
    private static void CheckPlayerCollisions(
        IReadOnlyList<CourtAssignment> planned,
        SchedulingContext context,
        List<ScheduleViolation> violations)
    {
        var byPlayer = new Dictionary<Guid, List<(CourtAssignment Assignment, TimeSlot Slot)>>();

        foreach (var assignment in planned)
        {
            var match = MatchOf(assignment, context);
            if (match is null)
            {
                continue;
            }

            foreach (var playerId in PlayersOf(match, context))
            {
                if (!byPlayer.TryGetValue(playerId, out var list))
                {
                    byPlayer[playerId] = list = [];
                }

                list.Add((assignment, assignment.PlannedSlot!.Value));
            }
        }

        foreach (var (_, slots) in byPlayer)
        {
            var ordered = slots.OrderBy(s => s.Slot.Start).ToList();

            for (var index = 1; index < ordered.Count; index++)
            {
                var previous = ordered[index - 1];
                var current = ordered[index];

                if (previous.Slot.Overlaps(current.Slot))
                {
                    violations.Add(new ScheduleViolation(
                        ScheduleConstraint.PlayerDoubleBooked,
                        $"Ein Spieler soll zeitgleich zweimal antreten ({previous.Slot} und {current.Slot}).",
                        current.Assignment.Id));
                }
                else if (current.Slot.Start - previous.Slot.End < context.MinimumRestBetweenMatches)
                {
                    violations.Add(new ScheduleViolation(
                        ScheduleConstraint.PlayerRestPeriod,
                        $"Zwischen zwei Matches eines Spielers liegen weniger als " +
                        $"{context.MinimumRestBetweenMatches.TotalMinutes:0} Minuten Pause.",
                        current.Assignment.Id));
                }
            }
        }
    }

    /// <summary>
    /// Ein Match, das auf den Sieger eines anderen wartet, kann nicht vor dessen
    /// Ende beginnen.
    /// </summary>
    private static void CheckDependencies(
        CourtAssignment assignment,
        IReadOnlyList<CourtAssignment> planned,
        SchedulingContext context,
        List<ScheduleViolation> violations)
    {
        var match = MatchOf(assignment, context);
        if (match is null)
        {
            return;
        }

        foreach (var predecessorId in Predecessors(match))
        {
            var predecessor = planned.FirstOrDefault(a => a.MatchId == predecessorId);
            if (predecessor?.PlannedSlot is not { } predecessorSlot)
            {
                continue;
            }

            if (assignment.PlannedSlot!.Value.Start < predecessorSlot.End)
            {
                violations.Add(new ScheduleViolation(
                    ScheduleConstraint.DependencyOrder,
                    $"Dieses Match wartet auf ein Match, das erst um {predecessorSlot.End:HH:mm} endet.",
                    assignment.Id));
            }
        }
    }

    private static IEnumerable<Guid> Predecessors(Match match)
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

    private static IEnumerable<Guid> PlayersOf(Match match, SchedulingContext context)
    {
        foreach (var entryId in new[] { match.Side1.EntryId, match.Side2.EntryId })
        {
            if (entryId is { } id && context.PlayersByEntry.TryGetValue(id, out var players))
            {
                foreach (var playerId in players)
                {
                    yield return playerId;
                }
            }
        }
    }

    private static Match? MatchOf(CourtAssignment assignment, SchedulingContext context) =>
        context.Matches.FirstOrDefault(m => m.Id == assignment.MatchId);
}
