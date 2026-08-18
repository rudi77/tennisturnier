using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.PublicView;

/// <summary>
/// Die öffentliche Sicht auf ein Turnier (ADR-0003).
///
/// Diese Typen sind die Verbotsliste in ausführbarer Form: was hier kein Feld
/// hat, kann nicht öffentlich werden. Deshalb tragen sie ausschließlich Namen,
/// Ergebnisse und Zeiten — keine Kontaktdaten, keine Geburtsdaten, keine
/// internen Notizen und keine Ids von Personen.
///
/// Match-Ids stehen darin, weil ein Bracket ohne sie nicht zu verdrahten ist.
/// Meldungs-, Teilnehmer- und Spieler-Ids fehlen bewusst: sie sind für die
/// Anzeige entbehrlich und laden nur dazu ein, öffentliche mit internen Daten
/// zusammenzuführen.
/// </summary>
/// <param name="VenueName">
/// Die Anlage, auf der gespielt wird. Hieß einmal <c>ClubName</c> und war der
/// ausrichtende Verein; sie steht jetzt am Turnier selbst — mehr als ihr Name
/// gehört auch weiterhin nicht in eine öffentliche Antwort.
/// </param>
/// <param name="TimeZoneId">
/// Die Zone, in der die Zeiten dieser Antwort zu lesen sind. Sie steht darin,
/// weil die Antwort sonst unvollständig ist: alle Zeiten sind UTC, und ein
/// Zuschauer ohne Anmeldung hat keine zweite Quelle für die Zone der Anlage —
/// seine eigene ist es gerade nicht, sonst stünde am Handy eines Angereisten
/// eine andere Uhrzeit als am Aushang.
///
/// Verraten wird damit nichts, was der Name der Anlage nicht schon sagt.
/// </param>
public sealed record PublicTournamentView(
    Guid Id,
    string Name,
    string VenueName,
    string TimeZoneId,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    TournamentState State,
    SchedulingMode SchedulingMode,
    IReadOnlyList<PublicPhaseView> Phases,
    IReadOnlyList<PublicCourtView> Courts);

public sealed record PublicPhaseView(
    Guid Id,
    int Ordinal,
    string Name,
    PhaseStatus Status,
    IReadOnlyList<PublicMatchView> Matches,
    IReadOnlyList<PublicStandingView> Standings);

/// <summary>
/// Ein Match in der öffentlichen Ansicht.
///
/// <see cref="EarliestStart"/> ist eine Zusage, <see cref="PlannedStart"/> eine
/// Schätzung. Beide getrennt zu führen ist keine Feinheit: am Turniertag ist die
/// Unterscheidung der ganze Unterschied zwischen einem verlässlichen Aushang und
/// einer Fiktion (ADR-0002).
/// </summary>
public sealed record PublicMatchView(
    Guid Id,
    int Round,
    int Position,
    string? Label,
    string? Group,
    PublicSideView Side1,
    PublicSideView Side2,
    MatchStatus Status,
    MatchOutcome? Outcome,
    int? WinnerSide,
    string? Score,
    string? CourtName,
    DateTimeOffset? EarliestStart,
    DateTimeOffset? PlannedStart,
    AssignmentStatus? AssignmentStatus);

/// <summary>
/// Eine Seite eines Matches. <see cref="Name"/> steht erst, wenn feststeht, wer
/// spielt; bis dahin sagt <see cref="Origin"/>, worauf gewartet wird —
/// „Sieger aus Halbfinale 1".
/// </summary>
public sealed record PublicSideView(string? Name, int? Seed, string Origin);

public sealed record PublicStandingView(
    int Rank,
    string Name,
    string? Group,
    int Played,
    int Won,
    int Lost,
    int Points,
    int SetsWon,
    int SetsLost,
    int GamesWon,
    int GamesLost);

/// <summary>
/// Ein Platz mit seiner Warteschlange. Die Reihenfolge ist die Aussage — eine
/// Uhrzeit wäre am Turniertag geraten (ADR-0002).
/// </summary>
public sealed record PublicCourtView(
    Guid Id,
    string Name,
    IReadOnlyList<PublicCourtSlotView> Queue);

public sealed record PublicCourtSlotView(
    Guid MatchId,
    int SequenceOnCourt,
    AssignmentStatus Status,
    DateTimeOffset? EarliestStart,
    DateTimeOffset? PlannedStart);
