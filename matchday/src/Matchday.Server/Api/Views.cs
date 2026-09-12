using Matchday.Domain;

namespace Matchday.Server.Api;

/// <summary>
/// Die Sicht auf ein Turnier, wie sie die Oberfläche und die Widgets bekommen.
/// Datensparsam: kein Token, keine Eigentümerkennung (ADR-0003, vereinfacht).
/// </summary>
public sealed record TournamentView(
    Guid Id,
    string Name,
    DateOnly? Date,
    string? Location,
    Mode Mode,
    Discipline Discipline,
    MatchFormat Format,
    string FormatText,
    TournamentState State,
    IReadOnlyList<Participant> Participants,
    IReadOnlyList<MatchView> Matches,
    IReadOnlyList<Standing> Standings,
    int Rounds);

public sealed record MatchView(
    Guid Id,
    int Round,
    int Position,
    string Label,
    SideView Side1,
    SideView Side2,
    MatchStatus Status,
    bool IsBye,
    ScoreView? Score);

public sealed record SideView(SideKind Kind, Guid? ParticipantId, string Name);

public sealed record ScoreView(MatchOutcome Outcome, int WinnerSide, IReadOnlyList<SetScore> Sets, string Text);

public sealed record TournamentSummary(Guid Id, string Name, DateOnly? Date, string? Location, Mode Mode, Discipline Discipline, TournamentState State, int ParticipantCount, string AdminToken);

/// <summary>Die beiden Links: einer zum Mitschauen, einer zum Verwalten.</summary>
public sealed record TournamentLinks(string PublicUrl, string AdminUrl);

public static class ViewBuilder
{
    public static TournamentView Build(Tournament t) => new(
        t.Id,
        t.Name,
        t.Date,
        t.Location,
        t.Mode,
        t.Discipline,
        t.Format,
        t.Format.Describe(),
        t.State,
        t.Participants,
        t.Matches.Select(m => new MatchView(
            m.Id,
            m.Round,
            m.Position,
            m.Label,
            new SideView(m.Side1.Kind, m.Side1.ParticipantId, t.NameOf(m.Side1)),
            new SideView(m.Side2.Kind, m.Side2.ParticipantId, t.NameOf(m.Side2)),
            m.Status,
            m.IsBye,
            m.Score is null ? null : new ScoreView(m.Score.Outcome, m.Score.WinnerSide, m.Score.Sets, m.Score.ToString())))
        .ToList(),
        t.Standings(),
        t.Matches.Count == 0 ? 0 : t.Matches.Max(m => m.Round));

    public static TournamentSummary Summarize(Tournament t) => new(
        t.Id, t.Name, t.Date, t.Location, t.Mode, t.Discipline, t.State, t.Participants.Count, t.AdminToken);

    public static TournamentLinks Links(Tournament t, string baseUrl) => new(
        $"{baseUrl}/?t={t.Id}",
        $"{baseUrl}/?a={t.AdminToken}");
}
