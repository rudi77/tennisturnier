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
    ScoreView? Score,
    LiveView? Live = null,
    int LiveEvents = 0);

public sealed record SideView(SideKind Kind, Guid? ParticipantId, string Name);

public sealed record ScoreView(MatchOutcome Outcome, int WinnerSide, IReadOnlyList<SetScore> Sets, string Text);

/// <summary>
/// Der laufende Stand, fertig zum Zeigen: die Sätze bis hierher samt dem
/// laufenden, und die Punkte des laufenden Spiels, wie man sie ansagt.
/// <c>Running</c> sagt, ob der letzte Satz in <c>Sets</c> noch läuft — direkt
/// nach einem Satzende steht dort nur, was fertig ist.
/// </summary>
public sealed record LiveView(
    IReadOnlyList<SetScore> Sets,
    int Games1,
    int Games2,
    string Points1,
    string Points2,
    bool InTiebreak,
    bool InMatchTiebreak,
    int Events,
    bool Running);

public sealed record TournamentSummary(Guid Id, string Name, DateOnly? Date, string? Location, Mode Mode, Discipline Discipline, TournamentState State, int ParticipantCount, string AdminToken);

/// <summary>Die Links: einer zum Mitschauen, einer zum Eintragen, einer zum Verwalten.</summary>
public sealed record TournamentLinks(string PublicUrl, string AdminUrl, string ScorerUrl);

/// <summary>Was der Eintragen-Link bekommt: die Sicht und sein Token — nie das der Verwaltung.</summary>
public sealed record ScorerAccess(TournamentView Tournament, string ScorerToken);

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
            m.Score is null ? null : new ScoreView(m.Score.Outcome, m.Score.WinnerSide, m.Score.Sets, m.Score.ToString()),
            LiveOf(t, m),
            m.Live?.Count ?? 0))
        .ToList(),
        t.Standings(),
        t.Matches.Count == 0 ? 0 : t.Matches.Max(m => m.Round));

    public static TournamentSummary Summarize(Tournament t) => new(
        t.Id, t.Name, t.Date, t.Location, t.Mode, t.Discipline, t.State, t.Participants.Count, t.AdminToken);

    public static TournamentLinks Links(Tournament t, string baseUrl) => new(
        $"{baseUrl}/?t={t.Id}",
        $"{baseUrl}/?a={t.AdminToken}",
        $"{baseUrl}/?s={t.ScorerToken}");

    /// <summary>Solange gezählt wird, der Stand; ist das Match vorbei, sagt das Ergebnis alles.</summary>
    private static LiveView? LiveOf(Tournament t, Match m)
    {
        if (m.Score is not null || t.LiveStateOf(m) is not { } s)
        {
            return null;
        }

        var running = s.Games1 + s.Games2 + s.Points1 + s.Points2 > 0;
        IReadOnlyList<SetScore> sets = running
            ? [.. s.CompletedSets, new SetScore(s.InMatchTiebreak ? s.Points1 : s.Games1, s.InMatchTiebreak ? s.Points2 : s.Games2)]
            : s.CompletedSets;

        return new LiveView(sets, s.Games1, s.Games2, s.PointsText(1), s.PointsText(2), s.InTiebreak, s.InMatchTiebreak, m.Live!.Count, running);
    }
}
