namespace TennisTurnier.Domain.Scheduling;

/// <summary>Was mit einer einzelnen Zuweisung geschieht, wenn der Vorschlag angenommen wird.</summary>
public enum ProposalChange
{
    /// <summary>Bleibt, wie sie ist.</summary>
    Unchanged,

    /// <summary>Wird neu angelegt.</summary>
    Added,

    /// <summary>Wechselt Platz oder Zeit.</summary>
    Moved,
}

/// <summary>
/// Eine vorgeschlagene Ansetzung samt Begründung.
///
/// Die Begründung ist kein Beiwerk. Sie ist der Unterschied zwischen einem
/// Werkzeug, das die Turnierleitung benutzt, und einem, das sie umgeht: wer
/// nicht sieht, warum ein Match dort liegt, wo es liegt, verschiebt es lieber
/// von Hand — und schaltet die Automatik beim nächsten Turnier ab.
/// </summary>
public sealed record ProposedAssignment(
    Guid MatchId,
    Guid CourtId,
    string CourtName,
    int SequenceOnCourt,
    DateTimeOffset PlannedStart,
    TimeSpan EstimatedDuration,
    ProposalChange Change,
    string Reason)
{
    public DateTimeOffset PlannedEnd => PlannedStart + EstimatedDuration;
}

/// <summary>Ein Match, für das kein Platz gefunden wurde — samt Grund.</summary>
public sealed record UnscheduledMatch(Guid MatchId, string Reason);

/// <summary>
/// Wie stark der Vorschlag den bestehenden Plan umwirft.
///
/// Der wichtigste Wert ist <see cref="Moved"/>. Ein Solver, der nach jeder
/// kleinen Änderung den halben Plan neu legt, wird abgeschaltet — die
/// Turnierleitung hat den alten Aushang schon verteilt (ADR-0002).
/// </summary>
public sealed record ScheduleDiff(int Unchanged, int Added, int Moved, int Removed)
{
    public int Total => Unchanged + Added + Moved;

    public bool IsEmpty => Added == 0 && Moved == 0 && Removed == 0;
}

/// <summary>
/// Das Ergebnis eines Solverlaufs. Ein Vorschlag, kein Plan: er wird erst
/// wirksam, wenn die Turnierleitung ihn bestätigt (ADR-0002).
/// </summary>
public sealed record ScheduleProposal(
    IReadOnlyList<ProposedAssignment> Assignments,
    IReadOnlyList<UnscheduledMatch> Unscheduled,
    IReadOnlyList<ScheduleViolation> Violations,
    ScheduleDiff Diff)
{
    public static ScheduleProposal Empty { get; } =
        new([], [], [], new ScheduleDiff(0, 0, 0, 0));
}
