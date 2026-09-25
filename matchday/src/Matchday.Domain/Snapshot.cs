namespace Matchday.Domain;

/// <summary>Das Turnier als reine Daten — was der Speicher schreibt und liest.</summary>
public sealed record TournamentSnapshot(
    Guid Id,
    string Name,
    DateOnly? Date,
    string? Location,
    Mode Mode,
    Discipline Discipline,
    MatchFormat Format,
    TournamentState State,
    string OwnerId,
    string AdminToken,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Participant> Participants,
    IReadOnlyList<MatchSnapshot> Matches,
    TimeOnly? StartTime = null,
    DateTimeOffset? StartedAt = null,
    string? ViewerToken = null);

public sealed record MatchSnapshot(
    Guid Id,
    int Round,
    int Position,
    string Label,
    Side Side1,
    Side Side2,
    ScoreSnapshot? Score,
    IReadOnlyList<LiveEvent>? Live = null);

public sealed record ScoreSnapshot(
    MatchOutcome Outcome,
    int WinnerSide,
    IReadOnlyList<SetScore> CompletedSets,
    SetScore? AbandonedSet);
