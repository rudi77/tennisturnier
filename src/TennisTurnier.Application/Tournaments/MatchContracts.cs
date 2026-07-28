using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Application.Tournaments;

/// <summary>Ein Match, wie es die Turnierleitung sieht.</summary>
public sealed record MatchDetail(
    Guid Id,
    Guid PhaseId,
    int Round,
    int Position,
    string? Label,
    string? Group,
    MatchSideDetail Side1,
    MatchSideDetail Side2,
    MatchStatus Status,
    ScoreDetail? Score,
    CourtAssignmentDetail? Assignment,
    int Version);

/// <summary>
/// Eine Match-Seite mit ihrer Herkunft im Klartext.
///
/// <see cref="Origin"/> steht dort, wo noch niemand feststeht — „Sieger aus
/// Halbfinale 1". Genau das macht ein Bracket lesbar, bevor gespielt wurde.
/// </summary>
public sealed record MatchSideDetail(Guid? EntryId, string? ParticipantName, string Origin);

/// <summary>
/// Ein Ergebnis für die Ausgabe.
///
/// Gespielte und abgebrochene Sätze stehen getrennt, wie in der Domäne: läge der
/// abgebrochene auch in <see cref="CompletedSets"/>, zeigte ihn ein Client
/// doppelt an, und wer daraus Satzgewinne zählt, zählte einen Satz mit, den
/// niemand gewonnen hat.
/// </summary>
public sealed record ScoreDetail(
    MatchOutcome Outcome,
    int WinnerSide,
    IReadOnlyList<SetScore> CompletedSets,
    SetScore? AbandonedSet,
    string Display);

public sealed record CourtAssignmentDetail(
    Guid Id,
    Guid CourtId,
    string CourtName,
    int SequenceOnCourt,
    DateTimeOffset? PlannedStart,
    DateTimeOffset? EarliestStart,
    TimeSpan EstimatedDuration,
    DateTimeOffset? ActualStart,
    DateTimeOffset? ActualEnd,
    AssignmentSource Source,
    AssignmentStatus Status);

public sealed record PhaseDetail(
    Guid Id,
    int Ordinal,
    string Name,
    PhaseStatus Status,
    IReadOnlyList<MatchDetail> Matches);

/// <summary>Die Endplatzierung beziehungsweise Tabelle einer Phase.</summary>
public sealed record StandingsDetail(Guid PhaseId, IReadOnlyList<Standing> Places);

// --- Ergebniseingabe --------------------------------------------------------

/// <summary>
/// Ein einzutragendes Ergebnis.
///
/// Der Ausgang steht voran, weil er bestimmt, welche der übrigen Angaben
/// überhaupt gebraucht werden: ein Nichtantreten hat keinen Spielstand, eine
/// Aufgabe einen unvollständigen.
/// </summary>
public sealed record RecordResultRequest(
    MatchOutcome Outcome,
    IReadOnlyList<SetScore>? Sets = null,
    SetScore? AbandonedSet = null,
    int? AffectedSide = null);

// --- Platzzuweisung ---------------------------------------------------------

public sealed record AssignCourtRequest(
    Guid CourtId,
    int SequenceOnCourt,
    DateTimeOffset? PlannedStart,
    DateTimeOffset? EarliestStart,
    TimeSpan? EstimatedDuration,
    bool Pinned = false);

/// <summary>
/// Das Ergebnis einer Zuweisung samt der Verstöße, die sie erzeugt.
///
/// Verstöße blockieren die Zuweisung bewusst nicht: die Turnierleitung kennt
/// Umstände, die das System nicht kennt, und muss einen Platz auch gegen die
/// Regel vergeben können. Sie soll nur wissen, was sie tut (ADR-0002).
/// </summary>
public sealed record AssignCourtResult(Guid AssignmentId, IReadOnlyList<ScheduleViolationDetail> Violations);

public sealed record ScheduleViolationDetail(ScheduleConstraint Constraint, string Message, Guid AssignmentId);
