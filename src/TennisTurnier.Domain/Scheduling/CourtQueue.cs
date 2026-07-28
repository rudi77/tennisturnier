using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Scheduling;

/// <summary>
/// Die Warteschlange eines Platzes am Turniertag (ADR-0002).
///
/// Die harte Randbedingung beim Tennis ist, dass die Matchdauer unbekannt ist.
/// Ein starres Zeitraster kippt beim ersten langen Match, und danach ist der
/// ganze Aushang Fiktion. Deshalb ist die Reihenfolge auf dem Platz die
/// Aussage — und die geschätzten Zeiten dahinter werden nachgezogen, sobald ein
/// Match tatsächlich endet.
/// </summary>
public static class CourtQueue
{
    /// <summary>
    /// Wechselpause zwischen zwei Matches auf demselben Platz. Dieselbe Zahl wie
    /// im Solver — abgehen, abziehen, aufwärmen.
    /// </summary>
    public static readonly TimeSpan Turnaround = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Zieht die Schätzungen der wartenden Matches eines Platzes nach.
    ///
    /// Gerechnet wird von dem Zeitpunkt an, ab dem der Platz frei ist: dem Ende
    /// des laufenden Matches, sonst der aktuellen Uhrzeit. Eine Zusage
    /// (<see cref="CourtAssignment.EarliestStart"/>) wird dabei nie unterlaufen —
    /// sie ist das Einzige, worauf sich ein Spieler verlassen kann, und ein
    /// vorgezogener Aufruf wäre genau der Vertrauensbruch, den ADR-0002
    /// vermeiden will.
    ///
    /// Gibt zurück, wie viele Schätzungen sich geändert haben.
    /// </summary>
    public static int Reflow(
        IReadOnlyList<CourtAssignment> onCourt,
        DateTimeOffset freeFrom,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(onCourt);

        var next = Later(freeFrom, now);
        var changed = 0;

        var position = 0;

        // Das laufende oder aufgerufene Match bindet den Platz; erst danach
        // beginnt die Warteschlange.
        foreach (var assignment in Waiting(onCourt))
        {
            var start = Later(next, assignment.EarliestStart ?? next);

            if (assignment.PlannedStart != start)
            {
                assignment.PlanFor(start);
                changed++;
            }

            // Lückenlos ab 1 durchnummerieren: sobald ein Match den Platz
            // verlässt, rückt der Rest auf. „Sie sind der Dritte auf Platz 2" ist
            // eine Auskunft, und sie wäre falsch, wenn die Nummer die des
            // ursprünglichen Plans bliebe.
            if (assignment.SequenceOnCourt != ++position)
            {
                assignment.SetSequence(position);
            }

            next = start + assignment.EstimatedDuration + Turnaround;
        }

        return changed;
    }

    /// <summary>
    /// Ab wann der Platz wieder frei ist: das geschätzte Ende dessen, was gerade
    /// darauf läuft. Läuft nichts, ist er sofort frei.
    /// </summary>
    public static DateTimeOffset FreeFrom(IReadOnlyList<CourtAssignment> onCourt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(onCourt);

        var busy = onCourt
            .Where(a => a.Status is AssignmentStatus.Called or AssignmentStatus.Running)
            .ToList();

        if (busy.Count == 0)
        {
            return now;
        }

        return busy
            .Select(a => Later((a.ActualStart ?? a.PlannedStart ?? now) + a.EstimatedDuration, now) + Turnaround)
            .Max();
    }

    /// <summary>
    /// Die wartenden Zuweisungen in ihrer Reihenfolge. Beendete und unterbrochene
    /// gehören nicht dazu: eine unterbrochene wartet nicht auf ihren Platz,
    /// sondern auf ihre Fortsetzung, und die kann anderswo stattfinden.
    /// </summary>
    public static IReadOnlyList<CourtAssignment> Waiting(IReadOnlyList<CourtAssignment> onCourt)
    {
        ArgumentNullException.ThrowIfNull(onCourt);

        return
        [
            .. onCourt
                .Where(a => a.Status == AssignmentStatus.Planned)
                .OrderBy(a => a.SequenceOnCourt)
                .ThenBy(a => a.PlannedStart ?? DateTimeOffset.MaxValue),
        ];
    }

    /// <summary>
    /// Bringt die Warteschlange in die angegebene Reihenfolge und nummeriert sie
    /// lückenlos ab 1 durch.
    ///
    /// Lückenlos, weil die Nummer am Turniertag vorgelesen wird: „Sie sind der
    /// Dritte auf Platz 2" ist eine Auskunft, keine Sortierhilfe.
    /// </summary>
    public static void Reorder(IReadOnlyList<CourtAssignment> onCourt, IReadOnlyList<Guid> order)
    {
        ArgumentNullException.ThrowIfNull(onCourt);
        ArgumentNullException.ThrowIfNull(order);

        var waiting = Waiting(onCourt);
        var byId = waiting.ToDictionary(a => a.Id);

        if (order.Count != waiting.Count || order.Distinct().Count() != order.Count
            || order.Any(id => !byId.ContainsKey(id)))
        {
            throw new DomainException(
                $"Die neue Reihenfolge muss genau die {waiting.Count} wartenden Zuweisungen dieses Platzes " +
                "nennen, jede genau einmal.");
        }

        var sequence = 0;

        foreach (var id in order)
        {
            byId[id].SetSequence(++sequence);
        }
    }

    /// <summary>
    /// Wie nah eine Zuweisung am Geschehen ist: laufend vor aufgerufen vor
    /// wartend vor unterbrochen vor beendet.
    ///
    /// Eine Rangfolge und nicht die Reihenfolge des Aufzählungstyps: dort steht
    /// <c>Called</c> vor <c>Running</c>, und wer danach sortiert, hält das eben
    /// aufgerufene nächste Match für das laufende — das laufende verschwindet
    /// dann aus der Platzübersicht und lässt sich nicht mehr beenden.
    /// </summary>
    public static int Liveness(CourtAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return assignment.Status switch
        {
            AssignmentStatus.Running => 0,
            AssignmentStatus.Called => 1,
            AssignmentStatus.Planned => 2,
            AssignmentStatus.Suspended => 3,
            _ => 4,
        };
    }

    private static DateTimeOffset Later(DateTimeOffset left, DateTimeOffset right) =>
        left > right ? left : right;
}
