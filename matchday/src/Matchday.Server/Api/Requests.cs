using Matchday.Domain;

namespace Matchday.Server.Api;

/// <summary>Wer handelt: die Browserkennung und, falls über den Verwalterlink gekommen, das Token.</summary>
public sealed record Actor(string ClientId, string? AdminToken)
{
    public bool MayManage(Tournament t) =>
        t.OwnerId == ClientId || (AdminToken is not null && AdminToken == t.AdminToken);
}

public sealed record CreateTournamentRequest(
    string Name,
    DateOnly? Date = null,
    string? Location = null,
    Mode Mode = Mode.Knockout,
    MatchFormat? Format = null,
    Discipline Discipline = Discipline.Singles,
    IReadOnlyList<string>? Participants = null);

public sealed record UpdateTournamentRequest(
    string? Name = null,
    DateOnly? Date = null,
    bool ClearDate = false,
    string? Location = null,
    bool ClearLocation = false,
    Mode? Mode = null,
    Discipline? Discipline = null,
    MatchFormat? Format = null);

public sealed record AddParticipantsRequest(IReadOnlyList<string> Names);

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
