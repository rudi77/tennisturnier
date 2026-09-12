namespace Matchday.Domain;

/// <summary>
/// Ein Satzergebnis. <see cref="TiebreakPoints"/> ist der Punktestand des
/// Unterlegenen im Tiebreak — bei 7:6 (7:5) also die 5.
/// </summary>
public sealed record SetScore(int Games1, int Games2, int? TiebreakPoints = null)
{
    public int WinnerSide => Games1 > Games2 ? 1 : 2;

    public override string ToString() =>
        TiebreakPoints is { } points ? $"{Games1}:{Games2} ({points})" : $"{Games1}:{Games2}";
}

/// <summary>Wie ein Match ausgegangen ist.</summary>
public enum MatchOutcome
{
    /// <summary>Regulär ausgespielt.</summary>
    Normal,

    /// <summary>Aufgabe während des Matches. Der begonnene Spielstand zählt.</summary>
    Retirement,

    /// <summary>Nicht angetreten. Kein Spielstand.</summary>
    Walkover,

    /// <summary>Freilos — der Gegner ist kampflos weiter, gespielt wurde nie.</summary>
    Bye,
}

/// <summary>
/// Das Ergebnis eines Matches: Ausgang, Sieger und — sofern gespielt wurde —
/// die Sätze. Die Prüfregeln sind aus dem alten Baum übernommen.
/// </summary>
public sealed record Score
{
    private Score(MatchOutcome outcome, int winnerSide, IReadOnlyList<SetScore> completedSets, SetScore? abandonedSet)
    {
        Outcome = outcome;
        WinnerSide = winnerSide;
        CompletedSets = completedSets;
        AbandonedSet = abandonedSet;
    }

    public MatchOutcome Outcome { get; }

    /// <summary>1 oder 2 — welche Seite gewonnen hat.</summary>
    public int WinnerSide { get; }

    /// <summary>Die zu Ende gespielten Sätze.</summary>
    public IReadOnlyList<SetScore> CompletedSets { get; }

    /// <summary>
    /// Der beim Abbruch laufende Satz, sofern es einen gab. Er steht getrennt:
    /// niemand hat ihn gewonnen, und in der Tabelle darf er nicht als Satz zählen.
    /// </summary>
    public SetScore? AbandonedSet { get; }

    /// <summary>Alle Sätze in gespielter Reihenfolge, für die Anzeige.</summary>
    public IReadOnlyList<SetScore> Sets =>
        AbandonedSet is null ? CompletedSets : [.. CompletedSets, AbandonedSet];

    public int LoserSide => WinnerSide == 1 ? 2 : 1;

    public int SetsWonBy(int side) => CompletedSets.Count(s => s.WinnerSide == side);

    public int GamesWonBy(int side) => Sets.Sum(s => side == 1 ? s.Games1 : s.Games2);

    /// <summary>Regulär ausgespieltes Ergebnis.</summary>
    public static Score Played(IReadOnlyList<SetScore> sets, MatchFormat format)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(format);

        ValidateCompletedSets(sets, format);
        RequireDecided(sets, format);

        return new Score(MatchOutcome.Normal, DecideWinner(sets, format), sets, abandonedSet: null);
    }

    /// <summary>Aufgabe. Der bis dahin gespielte Stand wird festgehalten.</summary>
    public static Score Retired(IReadOnlyList<SetScore> completedSets, SetScore? abandonedSet, int retiringSide, MatchFormat format)
    {
        ArgumentNullException.ThrowIfNull(completedSets);
        ArgumentNullException.ThrowIfNull(format);
        RequireSide(retiringSide);

        ValidateCompletedSets(completedSets, format);

        if (IsDecided(completedSets, format))
        {
            throw new DomainException("Ein bereits entschiedenes Match lässt sich nicht mehr aufgeben.");
        }

        if (abandonedSet is not null)
        {
            ValidateAbandonedSet(abandonedSet, completedSets.Count, format);
        }

        return new Score(MatchOutcome.Retirement, Other(retiringSide), completedSets, abandonedSet);
    }

    /// <summary>Nicht angetreten. Ohne Spielstand.</summary>
    public static Score Walkover(int absentSide)
    {
        RequireSide(absentSide);
        return new Score(MatchOutcome.Walkover, Other(absentSide), [], abandonedSet: null);
    }

    /// <summary>Freilos: der Gegner steht kampflos in der nächsten Runde.</summary>
    public static Score ByeFor(int advancingSide)
    {
        RequireSide(advancingSide);
        return new Score(MatchOutcome.Bye, advancingSide, [], abandonedSet: null);
    }

    /// <summary>Baut ein bereits geprüftes Ergebnis wieder auf — für den Speicher.</summary>
    public static Score Rehydrate(MatchOutcome outcome, int winnerSide, IReadOnlyList<SetScore> completedSets, SetScore? abandonedSet)
    {
        ArgumentNullException.ThrowIfNull(completedSets);
        RequireSide(winnerSide);

        return new Score(outcome, winnerSide, completedSets, abandonedSet);
    }

    private static void RequireSide(int side)
    {
        if (side is not (1 or 2))
        {
            throw new DomainException($"Eine Seite ist 1 oder 2, war {side}.");
        }
    }

    private static int Other(int side) => side == 1 ? 2 : 1;

    private static int DecideWinner(IReadOnlyList<SetScore> sets, MatchFormat format) =>
        sets.Count(s => s.WinnerSide == 1) == format.SetsToWin ? 1 : 2;

    private static void ValidateCompletedSets(IReadOnlyList<SetScore> sets, MatchFormat format)
    {
        if (sets.Count > format.BestOf)
        {
            throw new DomainException($"Ein Match über {format.BestOf} Sätze kann nicht {sets.Count} Sätze haben.");
        }

        for (var index = 0; index < sets.Count; index++)
        {
            ValidateSet(sets[index], index, format);
        }
    }

    private static bool IsDecided(IReadOnlyList<SetScore> sets, MatchFormat format) =>
        sets.Count(s => s.WinnerSide == 1) >= format.SetsToWin
        || sets.Count(s => s.WinnerSide == 2) >= format.SetsToWin;

    private static void RequireDecided(IReadOnlyList<SetScore> sets, MatchFormat format)
    {
        var wonByOne = sets.Count(s => s.WinnerSide == 1);
        var wonByTwo = sets.Count - wonByOne;

        if (wonByOne != format.SetsToWin && wonByTwo != format.SetsToWin)
        {
            throw new DomainException(
                $"Nach {sets.Count} Sätzen hat niemand die nötigen {format.SetsToWin} Sätze gewonnen (Stand {wonByOne}:{wonByTwo}).");
        }

        var decidedAfter = DecidingSetIndex(sets, format.SetsToWin);
        if (decidedAfter < sets.Count - 1)
        {
            throw new DomainException(
                $"Das Match war nach Satz {decidedAfter + 1} entschieden, es sind aber {sets.Count} Sätze eingetragen.");
        }
    }

    private static void ValidateAbandonedSet(SetScore set, int completedCount, MatchFormat format)
    {
        var position = $"Satz {completedCount + 1}";

        if (set.Games1 < 0 || set.Games2 < 0)
        {
            throw new DomainException($"{position}: negative Spielstände gibt es nicht ({set}).");
        }

        if (set.TiebreakPoints is not null)
        {
            throw new DomainException($"{position}: ein abgebrochener Satz hat kein Tiebreak-Ergebnis ({set}).");
        }

        var isFinalSet = completedCount == format.BestOf - 1;
        var isMatchTiebreak = isFinalSet && format.BestOf > 1 && format.FinalSetMode == FinalSetMode.MatchTiebreak10;
        var allowTiebreak = !isFinalSet || format.FinalSetMode != FinalSetMode.Advantage;

        if (IsSetOver(set, format, isMatchTiebreak, allowTiebreak))
        {
            throw new DomainException($"{position}: dieser Satz war zu Ende gespielt und gehört zu den gespielten Sätzen ({set}).");
        }
    }

    private static bool IsSetOver(SetScore set, MatchFormat format, bool isMatchTiebreak, bool allowTiebreak)
    {
        var winner = Math.Max(set.Games1, set.Games2);
        var loser = Math.Min(set.Games1, set.Games2);

        if (isMatchTiebreak)
        {
            return winner >= 10 && winner - loser >= 2;
        }

        // Mit Tiebreak endet der Satz spätestens bei 7:5 oder 7:6; ohne ihn geht
        // er weiter, bis zwei Spiele Vorsprung stehen.
        return allowTiebreak
            ? (winner == format.TiebreakAt && winner - loser >= 2) || winner > format.TiebreakAt
            : winner >= format.TiebreakAt && winner - loser >= 2;
    }

    private static int DecidingSetIndex(IReadOnlyList<SetScore> sets, int setsToWin)
    {
        int one = 0, two = 0;
        var index = 0;

        while (one < setsToWin && two < setsToWin)
        {
            if (sets[index].WinnerSide == 1)
            {
                one++;
            }
            else
            {
                two++;
            }

            index++;
        }

        return index - 1;
    }

    private static void ValidateSet(SetScore set, int index, MatchFormat format)
    {
        var position = $"Satz {index + 1}";

        if (set.Games1 < 0 || set.Games2 < 0)
        {
            throw new DomainException($"{position}: negative Spielstände gibt es nicht ({set}).");
        }

        if (set.Games1 == set.Games2)
        {
            throw new DomainException($"{position}: ein Satz endet nicht unentschieden ({set}).");
        }

        // Ein Match über einen Satz hat keinen „letzten Satz“, der ein Tiebreak sein könnte.
        var isFinalSet = index == format.BestOf - 1;

        if (isFinalSet && format.BestOf > 1 && format.FinalSetMode == FinalSetMode.MatchTiebreak10)
        {
            ValidateMatchTiebreak(set, position);
            return;
        }

        ValidateRegularSet(set, position, format, allowTiebreak: !isFinalSet || format.FinalSetMode != FinalSetMode.Advantage);
    }

    private static void ValidateMatchTiebreak(SetScore set, string position)
    {
        var winner = Math.Max(set.Games1, set.Games2);
        var loser = Math.Min(set.Games1, set.Games2);

        if (winner < 10)
        {
            throw new DomainException($"{position}: ein Match-Tiebreak geht mindestens bis 10 ({set}).");
        }

        if (winner - loser < 2)
        {
            throw new DomainException($"{position}: ein Match-Tiebreak braucht zwei Punkte Vorsprung ({set}).");
        }

        if (winner > 10 && winner - loser != 2)
        {
            throw new DomainException($"{position}: nach der Verlängerung endet der Tiebreak bei genau zwei Punkten Vorsprung ({set}).");
        }
    }

    private static void ValidateRegularSet(SetScore set, string position, MatchFormat format, bool allowTiebreak)
    {
        var winner = Math.Max(set.Games1, set.Games2);
        var loser = Math.Min(set.Games1, set.Games2);
        var target = format.TiebreakAt;

        var isTiebreakSet = allowTiebreak && winner == target + 1 && loser == target;

        if (set.TiebreakPoints is not null && !isTiebreakSet)
        {
            throw new DomainException($"{position}: ein Tiebreak-Ergebnis gehört zu einem Satz {target + 1}:{target}, hier steht {set}.");
        }

        if (isTiebreakSet)
        {
            return;
        }

        if (winner < target)
        {
            throw new DomainException($"{position}: ein Satz geht mindestens bis {target} ({set}).");
        }

        if (winner == target && winner - loser < 2)
        {
            throw new DomainException($"{position}: bei {target} braucht der Satz zwei Spiele Vorsprung ({set}).");
        }

        // Mit Tiebreak ist {target+1}:{target-1} der einzige Stand über dem Ziel —
        // bei {target}:{target} wird der Tiebreak gespielt. Ohne Tiebreak geht es
        // weiter, bis genau zwei Spiele Vorsprung stehen: 8:6 ja, 8:5 nein.
        if (allowTiebreak && winner > target && !(winner == target + 1 && loser == target - 1))
        {
            throw new DomainException($"{position}: mit Tiebreak bei {target}:{target} endet ein Satz spätestens {target + 1}:{target} ({set}).");
        }

        if (winner > target && winner - loser != 2)
        {
            throw new DomainException($"{position}: in der Verlängerung endet der Satz mit genau zwei Spielen Vorsprung ({set}).");
        }
    }

    public bool Equals(Score? other) =>
        other is not null
        && Outcome == other.Outcome
        && WinnerSide == other.WinnerSide
        && AbandonedSet == other.AbandonedSet
        && CompletedSets.SequenceEqual(other.CompletedSets);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Outcome);
        hash.Add(WinnerSide);
        hash.Add(AbandonedSet);

        foreach (var set in CompletedSets)
        {
            hash.Add(set);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => Outcome switch
    {
        MatchOutcome.Normal => string.Join(", ", Sets),
        MatchOutcome.Retirement => Sets.Count == 0 ? "Aufgabe" : $"{string.Join(", ", Sets)} (Aufgabe)",
        MatchOutcome.Walkover => "kampflos",
        MatchOutcome.Bye => "Freilos",
        _ => Outcome.ToString(),
    };
}
