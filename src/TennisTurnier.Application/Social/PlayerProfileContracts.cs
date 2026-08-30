using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Social;

/// <summary>
/// Ein Spielerprofil, wie es genau ein Aufrufer sieht (ADR-0013).
///
/// Jede Zahl darin gilt relativ zu ihm: gerechnet wird über die Turniere, die
/// er ohnehin sehen darf. Zwei Personen bekommen zu demselben Spieler
/// verschiedene Bilanzen, und das ist die Aussage und nicht ihr Fehler.
/// </summary>
/// <param name="IsSelf">
/// Sieht der Aufrufer sich selbst? Nur dann darf die Oberfläche die Felder zum
/// Bearbeiten anbieten — ob es tatsächlich erlaubt ist, entscheidet weiterhin
/// der Anwendungsfall.
/// </param>
/// <param name="HasAccount">
/// Gehört der Spieler einem Konto? Wer aus einer hochgeladenen Liste kommt, hat
/// keines — er hat trotzdem eine Historie, aber niemanden, der über ihn
/// schreibt.
/// </param>
public sealed record PlayerProfileView(
    Guid PlayerId,
    string DisplayName,
    string FirstName,
    string LastName,
    string? Bio,
    string? HomeClub,
    bool IsSelf,
    bool HasAccount,
    PlayerRecordView Record,
    IReadOnlyList<PlayerTournamentView> Tournaments,
    IReadOnlyList<PlayerMatchView> Matches);

/// <summary>
/// Die Bilanz. Freilose zählen nicht mit — sie wurden nie gespielt.
/// </summary>
/// <param name="Tournaments">
/// Turniere mit mindestens einer Meldung, nicht mit mindestens einem Match: wer
/// gemeldet und nie gespielt hat, war trotzdem dabei.
/// </param>
public sealed record PlayerRecordView(
    int Played,
    int Won,
    int Lost,
    int Tournaments,
    int SetsWon,
    int SetsLost,
    DateOnly? LastPlayedOn);

public sealed record PlayerTournamentView(
    Guid TournamentId,
    string Name,
    Discipline Discipline,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    TournamentState State,
    EntryStatus Status,
    string ParticipantName,
    int Played,
    int Won);

/// <param name="Score">
/// Der Spielstand als fertige Zeichenkette. Er wird nur angezeigt und nie
/// gerechnet — die Sätze einzeln zu übertragen hieße, die Formatierung in jeder
/// Oberfläche noch einmal zu schreiben.
/// </param>
public sealed record PlayerMatchView(
    Guid MatchId,
    Guid TournamentId,
    string TournamentName,
    string PhaseName,
    string MatchName,
    string OwnName,
    string OpponentName,
    IReadOnlyList<PlayerLink> Opponents,
    PlayerLink? Partner,
    bool Won,
    MatchOutcome Outcome,
    string Score,
    DateTimeOffset? PlayedAt);

/// <summary>
/// Ein Spieler als Verweis: gerade so viel, dass die Oberfläche seinen Namen
/// anzeigen und sein Profil öffnen kann.
/// </summary>
public sealed record PlayerLink(Guid PlayerId, string DisplayName);

/// <summary>
/// Was jemand über sich selbst schreibt — samt seinem Namen.
///
/// Der Name steht mit darin, weil das Profil für viele die erste Stelle ist, an
/// der überhaupt ein Spieler zu ihrem Konto entsteht: wer beigetreten ist, ohne
/// zu melden, hat bis dahin keinen. Ihn aus dem Anzeigenamen des Ausstellers zu
/// raten, hieße „Anna Maria Müller-Berger" auf gut Glück zu zerlegen.
/// </summary>
public sealed record UpdateMyProfileRequest(
    string FirstName,
    string LastName,
    string? Bio,
    string? HomeClub);
