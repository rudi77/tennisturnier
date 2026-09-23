namespace Matchday.Domain;

public enum SideKind
{
    /// <summary>Noch niemand — die Quelle steht, das Ergebnis dort fehlt.</summary>
    WinnerOf,

    /// <summary>Ein Teilnehmer.</summary>
    Participant,

    /// <summary>Freilos.</summary>
    Bye,
}

/// <summary>Eine Seite eines Matches: ein Teilnehmer, der Sieger eines anderen Matches oder ein Freilos.</summary>
public sealed record Side(SideKind Kind, Guid? ParticipantId = null, Guid? SourceMatchId = null)
{
    public static Side Bye { get; } = new(SideKind.Bye);

    public static Side Of(Guid participantId) => new(SideKind.Participant, participantId);

    public static Side WinnerOf(Guid matchId) => new(SideKind.WinnerOf, SourceMatchId: matchId);

    public bool IsSettled => Kind == SideKind.Participant;
}

public enum MatchStatus
{
    /// <summary>Mindestens eine Seite steht noch nicht fest.</summary>
    Pending,

    /// <summary>Beide Seiten stehen, Ergebnis fehlt.</summary>
    Ready,

    /// <summary>Es wird gespielt: Der Stand läuft mit, ein Ergebnis gibt es noch nicht.</summary>
    Playing,

    Finished,
}

public sealed class Match
{
    internal Match(Guid id, int round, int position, string label, Side side1, Side side2, Score? score = null, IReadOnlyList<LiveEvent>? live = null)
    {
        Live = live is { Count: > 0 } ? [.. live] : null;
        Id = id;
        Round = round;
        Position = position;
        Label = label;
        Side1 = side1;
        Side2 = side2;
        Score = score;
    }

    public Guid Id { get; }

    public int Round { get; }

    public int Position { get; }

    public string Label { get; }

    public Side Side1 { get; private set; }

    public Side Side2 { get; private set; }

    public Score? Score { get; private set; }

    /// <summary>
    /// Was während des Spiels eingetragen wurde, Punkt für Punkt — oder null, wenn
    /// niemand mitzählt. Endet das Match auf diesem Weg, bleibt die Folge stehen,
    /// damit sich der letzte Punkt noch zurücknehmen lässt.
    /// </summary>
    public IReadOnlyList<LiveEvent>? Live { get; private set; }

    public MatchStatus Status => Score is not null
        ? MatchStatus.Finished
        : !(Side1.IsSettled && Side2.IsSettled) ? MatchStatus.Pending
        : Live is not null ? MatchStatus.Playing
        : MatchStatus.Ready;

    /// <summary>Hat hier schon jemand gespielt — ein Punkt oder ein Ergebnis, Freilose zählen nicht.</summary>
    public bool HasBegun => !IsBye && (Score is not null || Live is not null);

    /// <summary>
    /// Ein Freilos steht immer auf Seite 2: <see cref="KnockoutDraw"/> besetzt die
    /// geraden Positionen des Baums zuerst, und die sind nie überzählig. Auf
    /// Seite 1 steht also der, der weiterkommt.
    /// </summary>
    public bool IsBye => Side2.Kind == SideKind.Bye;

    public Guid? WinnerId => Score is null ? null : SideOf(Score.WinnerSide).ParticipantId;

    public Guid? LoserId => Score is null ? null : SideOf(Score.LoserSide).ParticipantId;

    public Side SideOf(int side) => side == 1 ? Side1 : Side2;

    public bool DependsOn(Guid matchId) => Side1.SourceMatchId == matchId || Side2.SourceMatchId == matchId;

    internal void SetScore(Score? score) => Score = score;

    internal void SetLive(IReadOnlyList<LiveEvent>? live) => Live = live is { Count: > 0 } ? [.. live] : null;

    /// <summary>Der Sieger des Quellmatches steht fest und rückt hier ein.</summary>
    internal void Resolve(Guid sourceMatchId, Guid participantId)
    {
        if (Side1.SourceMatchId == sourceMatchId)
        {
            Side1 = new Side(SideKind.Participant, participantId, sourceMatchId);
        }

        if (Side2.SourceMatchId == sourceMatchId)
        {
            Side2 = new Side(SideKind.Participant, participantId, sourceMatchId);
        }
    }

    /// <summary>Das Ergebnis des Quellmatches wurde zurückgenommen; die Seite ist wieder offen.</summary>
    internal void Unresolve(Guid sourceMatchId)
    {
        if (Side1.SourceMatchId == sourceMatchId)
        {
            Side1 = Side.WinnerOf(sourceMatchId);
        }

        if (Side2.SourceMatchId == sourceMatchId)
        {
            Side2 = Side.WinnerOf(sourceMatchId);
        }
    }
}
