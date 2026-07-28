using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;

namespace TennisTurnier.Domain.Matches;

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

/// <summary>
/// Wie ein Match ausgegangen ist.
///
/// Diese Aufzählung steht von Anfang an vollständig da, weil jeder dieser Fälle
/// sonst später als Sonderfall durch Tabellen, Bracket und Spielplan gezogen
/// werden müsste. Ein Turnier ohne Aufgabe und ohne Nichtantreten gibt es nicht.
/// </summary>
public enum MatchOutcome
{
    /// <summary>Regulär ausgespielt.</summary>
    Normal,

    /// <summary>Aufgabe während des Matches. Der begonnene Spielstand zählt.</summary>
    Retirement,

    /// <summary>Nicht angetreten. Kein Spielstand.</summary>
    Walkover,

    /// <summary>Disqualifikation.</summary>
    Disqualification,

    /// <summary>Freilos — der Gegner ist kampflos weiter, gespielt wurde nie.</summary>
    Bye,
}

/// <summary>
/// Das Ergebnis eines Matches: Ausgang, Sieger und — sofern gespielt wurde —
/// die Sätze.
/// </summary>
public sealed record Score
{
    private Score(
        MatchOutcome outcome,
        int winnerSide,
        IReadOnlyList<SetScore> completedSets,
        SetScore? abandonedSet)
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
    /// Der beim Abbruch laufende Satz, sofern es einen gab.
    ///
    /// Er steht bewusst getrennt: niemand hat ihn gewonnen. Läge er in derselben
    /// Liste, zählte ein Stand von 2:1 als gewonnener Satz — und die Tabelle
    /// wiese einen Satz aus, der nie zu Ende gespielt wurde.
    /// </summary>
    public SetScore? AbandonedSet { get; }

    /// <summary>Alle Sätze in gespielter Reihenfolge, für die Anzeige.</summary>
    public IReadOnlyList<SetScore> Sets =>
        AbandonedSet is null ? CompletedSets : [.. CompletedSets, AbandonedSet];

    public int LoserSide => WinnerSide == 1 ? 2 : 1;

    /// <summary>
    /// Gewonnene Sätze der angegebenen Seite. Der abgebrochene Satz zählt für
    /// niemanden.
    /// </summary>
    public int SetsWonBy(int side) => CompletedSets.Count(s => s.WinnerSide == side);

    /// <summary>
    /// Gewonnene Spiele der angegebenen Seite. Hier zählt der abgebrochene Satz
    /// mit — die Spiele darin wurden gespielt.
    /// </summary>
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

    /// <summary>
    /// Aufgabe. Der bis dahin gespielte Stand wird festgehalten — er zählt für
    /// Satz- und Spielverhältnis in der Tabelle, anders als beim Nichtantreten.
    /// </summary>
    public static Score Retired(
        IReadOnlyList<SetScore> completedSets,
        SetScore? abandonedSet,
        int retiringSide,
        MatchFormat format)
    {
        ArgumentNullException.ThrowIfNull(completedSets);
        ArgumentNullException.ThrowIfNull(format);
        RequireSide(retiringSide);

        ValidateCompletedSets(completedSets, format);

        // Ausdrücklich ohne Prüfung auf einen Sieger: bei einer Aufgabe ist das
        // Match gerade nicht zu Ende gespielt. Wäre es das, gäbe es nichts
        // aufzugeben — deshalb wird das hier auch abgewiesen.
        if (IsDecided(completedSets, format))
        {
            throw new DomainException(
                "Ein bereits entschiedenes Match lässt sich nicht mehr aufgeben.");
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

    public static Score Disqualified(int disqualifiedSide)
    {
        RequireSide(disqualifiedSide);
        return new Score(MatchOutcome.Disqualification, Other(disqualifiedSide), [], abandonedSet: null);
    }

    /// <summary>Freilos: der Gegner steht kampflos in der nächsten Runde.</summary>
    public static Score ByeFor(int advancingSide)
    {
        RequireSide(advancingSide);
        return new Score(MatchOutcome.Bye, advancingSide, [], abandonedSet: null);
    }

    /// <summary>
    /// Baut ein bereits geprüftes Ergebnis wieder auf — ausschließlich für den
    /// Persistenzadapter.
    ///
    /// Bewusst ohne erneute Prüfung. Die Regeln, gegen die geprüft wurde, stehen
    /// im eingefrorenen Format des Turniers und sind beim Laden eines einzelnen
    /// Matches nicht zur Hand. Sie zu erraten wäre schlimmer als sie wegzulassen:
    /// ein Turnier mit Kurzsätzen bis 4 speicherte ein gültiges 5:4, das gegen
    /// die geratenen Regeln eines Satzes bis 6 scheiterte — und das Match ließe
    /// sich nicht mehr laden.
    ///
    /// Die Prüfung gehört dorthin, wo Daten hereinkommen, nicht dorthin, wo sie
    /// wieder gelesen werden.
    /// </summary>
    public static Score Rehydrate(
        MatchOutcome outcome,
        int winnerSide,
        IReadOnlyList<SetScore> completedSets,
        SetScore? abandonedSet)
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

    private static int DecideWinner(IReadOnlyList<SetScore> sets, MatchFormat format)
    {
        var wonByOne = sets.Count(s => s.WinnerSide == 1);

        return wonByOne == format.SetsToWin ? 1 : 2;
    }

    private static void ValidateCompletedSets(IReadOnlyList<SetScore> sets, MatchFormat format)
    {
        if (sets.Count > format.BestOf)
        {
            throw new DomainException(
                $"Ein Match über {format.BestOf} Sätze kann nicht {sets.Count} Sätze haben.");
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
                $"Nach {sets.Count} Sätzen hat niemand die nötigen {format.SetsToWin} Sätze gewonnen " +
                $"(Stand {wonByOne}:{wonByTwo}).");
        }

        // Ein Match endet mit dem entscheidenden Satz. Steht danach noch einer,
        // ist entweder ein Satz zu viel eingetragen oder das Format falsch.
        var decidedAfter = DecidingSetIndex(sets, format.SetsToWin);
        if (decidedAfter < sets.Count - 1)
        {
            throw new DomainException(
                $"Das Match war nach Satz {decidedAfter + 1} entschieden, es sind aber {sets.Count} Sätze eingetragen.");
        }
    }

    /// <summary>
    /// Der beim Abbruch laufende Satz. Er muss möglich, aber gerade noch nicht
    /// zu Ende sein — sonst wäre er ein gespielter Satz und gehörte in die
    /// andere Liste.
    /// </summary>
    private static void ValidateAbandonedSet(SetScore set, int completedCount, MatchFormat format)
    {
        var position = $"Satz {completedCount + 1}";

        if (completedCount >= format.BestOf)
        {
            throw new DomainException(
                $"{position}: nach {completedCount} Sätzen kann kein weiterer begonnen haben.");
        }

        if (set.Games1 < 0 || set.Games2 < 0)
        {
            throw new DomainException($"{position}: negative Spielstände gibt es nicht ({set}).");
        }

        if (set.TiebreakPoints is not null)
        {
            throw new DomainException($"{position}: ein abgebrochener Satz hat kein Tiebreak-Ergebnis ({set}).");
        }

        var isMatchTiebreak = completedCount == format.BestOf - 1
                              && format.FinalSetMode == FinalSetMode.MatchTiebreak10;

        if (IsSetOver(set, format, isMatchTiebreak))
        {
            throw new DomainException(
                $"{position}: dieser Satz war zu Ende gespielt und gehört zu den gespielten Sätzen ({set}).");
        }
    }

    private static bool IsSetOver(SetScore set, MatchFormat format, bool isMatchTiebreak)
    {
        var winner = Math.Max(set.Games1, set.Games2);
        var loser = Math.Min(set.Games1, set.Games2);

        return isMatchTiebreak
            ? winner >= 10 && winner - loser >= 2
            : (winner >= format.TiebreakAt && winner - loser >= 2) || winner > format.TiebreakAt;
    }

    private static int DecidingSetIndex(IReadOnlyList<SetScore> sets, int setsToWin)
    {
        int one = 0, two = 0;

        for (var index = 0; index < sets.Count; index++)
        {
            if (sets[index].WinnerSide == 1)
            {
                one++;
            }
            else
            {
                two++;
            }

            if (one == setsToWin || two == setsToWin)
            {
                return index;
            }
        }

        return sets.Count - 1;
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

        var isFinalSet = index == format.BestOf - 1;

        if (isFinalSet && format.FinalSetMode == FinalSetMode.MatchTiebreak10)
        {
            ValidateMatchTiebreak(set, position);
            return;
        }

        ValidateRegularSet(set, position, format, allowTiebreak: !isFinalSet || format.FinalSetMode != FinalSetMode.Advantage);
    }

    /// <summary>
    /// Der Match-Tiebreak ersetzt den Entscheidungssatz und wird bis 10 gespielt,
    /// mit zwei Punkten Vorsprung.
    /// </summary>
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
            throw new DomainException(
                $"{position}: ein Tiebreak-Ergebnis gehört zu einem Satz {target + 1}:{target}, hier steht {set}.");
        }

        if (isTiebreakSet)
        {
            return;
        }

        if (winner < target)
        {
            throw new DomainException($"{position}: ein Satz geht mindestens bis {target} ({set}).");
        }

        // Bis zum Zielstand mit zwei Spielen Vorsprung, danach nur noch
        // Verlängerung um genau zwei — 8:6 ist gültig, 8:5 nicht.
        if (winner == target && winner - loser < 2)
        {
            throw new DomainException($"{position}: bei {target} braucht der Satz zwei Spiele Vorsprung ({set}).");
        }

        if (winner > target && winner - loser != 2)
        {
            throw new DomainException($"{position}: in der Verlängerung endet der Satz mit genau zwei Spielen Vorsprung ({set}).");
        }
    }

    /// <summary>
    /// Zwei Ergebnisse sind gleich, wenn sie dasselbe aussagen.
    ///
    /// Die vom Record erzeugte Gleichheit vergleicht die Satzliste über ihre
    /// Referenz und hielte damit zwei inhaltsgleiche Ergebnisse für verschieden.
    /// Das ist nicht bloß unschön: die Änderungsverfolgung der Persistenz baut
    /// darauf auf und schriebe sonst jedes geladene Match bei jedem Speichern
    /// erneut — auf SQLite, das datenbankweit serialisiert, spürbar.
    /// </summary>
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
        MatchOutcome.Retirement => Sets.Count == 0
            ? "Aufgabe"
            : $"{string.Join(", ", Sets)} (Aufgabe)",
        MatchOutcome.Walkover => "kampflos",
        MatchOutcome.Disqualification => "Disqualifikation",
        MatchOutcome.Bye => "Freilos",
        _ => Outcome.ToString(),
    };
}
