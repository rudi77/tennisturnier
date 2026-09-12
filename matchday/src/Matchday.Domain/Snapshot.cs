namespace Matchday.Domain;

/// <summary>Das Turnier als reine Daten — was der Speicher schreibt und liest.</summary>
public sealed record TournamentSnapshot(
    Guid Id,
    string Name,
    DateOnly? Date,
    string? Location,
    Mode Mode,
    MatchFormat Format,
    TournamentState State,
    string OwnerId,
    string AdminToken,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Participant> Participants,
    IReadOnlyList<MatchSnapshot> Matches);

public sealed record MatchSnapshot(
    Guid Id,
    int Round,
    int Position,
    string Label,
    Side Side1,
    Side Side2,
    ScoreSnapshot? Score);

public sealed record ScoreSnapshot(
    MatchOutcome Outcome,
    int WinnerSide,
    IReadOnlyList<SetScore> CompletedSets,
    SetScore? AbandonedSet);
