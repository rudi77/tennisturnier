using Matchday.Domain;

namespace Matchday.Server.Api;

/// <summary>
/// Wer handelt: die Browserkennung und, falls über einen Link gekommen, das
/// Token des Verwalterlinks oder des Eintragen-Links.
/// </summary>
public sealed record Actor(string ClientId, string? AdminToken, string? ScorerToken = null)
{
    public bool MayManage(Tournament t) =>
        t.OwnerId == ClientId || (AdminToken is not null && AdminToken == t.AdminToken);

    /// <summary>Spielstände und Ergebnisse eintragen: die Verwaltung, und wer den Eintragen-Link hat.</summary>
    public bool MayScore(Tournament t) =>
        MayManage(t) || (ScorerToken is not null && ScorerToken == t.ScorerToken);
}

public sealed record CreateTournamentRequest(
    string Name,
    DateOnly? Date = null,
    string? Location = null,
    Mode Mode = Mode.Knockout,
    MatchFormat? Format = null,
    Discipline Discipline = Discipline.Singles,
    IReadOnlyList<string>? Participants = null,
    TimeOnly? StartTime = null);

public sealed record UpdateTournamentRequest(
    string? Name = null,
    DateOnly? Date = null,
    bool ClearDate = false,
    string? Location = null,
    bool ClearLocation = false,
    Mode? Mode = null,
    Discipline? Discipline = null,
    MatchFormat? Format = null,
    TimeOnly? StartTime = null,
    bool ClearStartTime = false);

public sealed record AddParticipantsRequest(IReadOnlyList<string> Names);

/// <summary>Einzelne Spieler, aus denen das Los die Doppel-Teams bildet.</summary>
public sealed record RandomTeamsRequest(IReadOnlyList<string> Players);

public enum ResultKind
{
    Played,
    Walkover,
    Retired,
}

/// <summary>
/// Ein Ergebnis aus Sicht des Siegers: <c>Sets</c> als Spiele des Siegers und
/// des Verlierers. Bei Aufgabe und Nichtantreten ist <c>Winner</c> der, der
/// weiterkommt.
/// </summary>
public sealed record ResultRequest(
    ResultKind Kind,
    int WinnerSide,
    IReadOnlyList<SetScore> Sets,
    SetScore? AbandonedSet = null);

public enum LiveAction
{
    /// <summary>Ein Punkt für eine Seite.</summary>
    Point,

    /// <summary>Das laufende Spiel für eine Seite, ohne die Punkte einzeln.</summary>
    Game,

    /// <summary>Das letzte Eingetragene zurück.</summary>
    Undo,
}

/// <summary>Ein Schritt während des Matches. <c>Side</c> braucht es nur für Punkt und Spiel.</summary>
public sealed record LiveRequest(LiveAction Action, int Side = 0);
