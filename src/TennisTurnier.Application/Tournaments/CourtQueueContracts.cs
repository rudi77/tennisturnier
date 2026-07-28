using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Application.Tournaments;

/// <summary>
/// Ein Platz am Turniertag: was gerade darauf läuft und wer wartet.
/// </summary>
public sealed record CourtBoard(
    Guid CourtId,
    string CourtName,
    bool IsCenterCourt,
    QueuedMatch? Current,
    IReadOnlyList<QueuedMatch> Queue);

/// <summary>
/// Ein Match in der Warteschlange.
///
/// <see cref="EarliestStart"/> ist eine Zusage, <see cref="EstimatedStart"/> eine
/// Schätzung. Die beiden auseinanderzuhalten ist keine Feinheit: am Turniertag
/// ist es der Unterschied zwischen einer Auskunft, auf die sich ein Spieler
/// verlassen kann, und einer, die beim ersten langen Match hinfällig wird
/// (ADR-0002).
/// </summary>
public sealed record QueuedMatch(
    Guid AssignmentId,
    Guid MatchId,
    string? Label,
    string? Side1,
    string? Side2,
    int SequenceOnCourt,
    AssignmentStatus Status,

    /// <summary>
    /// Steht schon fest, wer antritt? Ein Match, dessen Vorspiel noch läuft, darf
    /// eingeplant, aber nicht aufgerufen werden.
    /// </summary>
    MatchStatus MatchStatus,
    DateTimeOffset? EarliestStart,
    DateTimeOffset? EstimatedStart,
    DateTimeOffset? ActualStart,
    TimeSpan EstimatedDuration,
    int Version);

/// <summary>Die neue Reihenfolge der wartenden Zuweisungen eines Platzes.</summary>
public sealed record ReorderQueueRequest(IReadOnlyList<Guid> AssignmentIds);

/// <summary>
/// Die Fortsetzung einer unterbrochenen Partie.
///
/// Der Platz darf ein anderer sein — nach Regen ist der alte womöglich noch
/// nass. Die unterbrochene Zuweisung bleibt als Historie stehen; erst beide
/// zusammen erzählen, was an diesem Tag passiert ist.
/// </summary>
public sealed record ResumeMatchRequest(Guid? CourtId = null);

/// <summary>Eine Zusage „nicht vor" für ein wartendes Match.</summary>
public sealed record PromiseStartRequest(DateTimeOffset EarliestStart);
