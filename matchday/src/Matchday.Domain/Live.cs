namespace Matchday.Domain;

/// <summary>Was während eines Matches eingetragen wird: ein Punkt oder gleich ein ganzes Spiel.</summary>
public enum LiveEventKind
{
    Point,

    /// <summary>
    /// Das laufende Spiel geht an eine Seite — für alle, die nicht jeden Punkt
    /// mitzählen wollen. Im Tiebreak gibt es keine Spiele, dort zählt es als Punkt.
    /// </summary>
    Game,
}

public sealed record LiveEvent(LiveEventKind Kind, int Side);

/// <summary>
/// Der Stand eines laufenden Matches. Er wird nie gespeichert, sondern immer aus
/// der Folge der Ereignisse abgespielt: Rückgängig heißt dann schlicht, das
/// letzte Ereignis wegzulassen, und kein Zwischenstand kann falsch stehen bleiben.
/// </summary>
public sealed record LiveState(
    IReadOnlyList<SetScore> CompletedSets,
    int Games1,
    int Games2,
    int Points1,
    int Points2,
    bool InTiebreak,
    bool InMatchTiebreak,
    int? WinnerSide)
{
    public bool IsDecided => WinnerSide is not null;

    /// <summary>
    /// Der Punktestand des laufenden Spiels, wie man ihn ansagt: 0, 15, 30, 40,
    /// „A“ für Vorteil. Im Tiebreak die Punkte selbst.
    /// </summary>
    public string PointsText(int side)
    {
        var own = side == 1 ? Points1 : Points2;
        var other = side == 1 ? Points2 : Points1;

        if (InTiebreak || InMatchTiebreak)
        {
            return own.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (own >= 3 && other >= 3)
        {
            return own > other ? "A" : "40";
        }

        return own switch
        {
            0 => "0",
            1 => "15",
            2 => "30",
            _ => "40",
        };
    }
}

/// <summary>Die Zählregeln des Tennis, für das Format des Turniers.</summary>
public static class LiveScoring
{
    public static LiveState Replay(IReadOnlyList<LiveEvent> events, MatchFormat format)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(format);

        var sets = new List<SetScore>();
        int games1 = 0, games2 = 0, points1 = 0, points2 = 0;
        var inTiebreak = false;
        int? winner = null;

        foreach (var e in events)
        {
            if (e.Side is not (1 or 2))
            {
                throw new DomainException($"Eine Seite ist 1 oder 2, war {e.Side}.");
            }

            if (winner is not null)
            {
                throw new DomainException("Das Match ist schon entschieden.");
            }

            var isFinalSet = sets.Count == format.BestOf - 1;
            var inMatchTiebreak = isFinalSet && format.BestOf > 1 && format.FinalSetMode == FinalSetMode.MatchTiebreak10;
            var allowTiebreak = !isFinalSet || format.FinalSetMode != FinalSetMode.Advantage;

            if (inMatchTiebreak || inTiebreak)
            {
                // Im Tiebreak ist jeder Eintrag ein Punkt.
                if (e.Side == 1)
                {
                    points1++;
                }
                else
                {
                    points2++;
                }

                var target = inMatchTiebreak ? 10 : 7;
                var high = Math.Max(points1, points2);

                if (high < target || Math.Abs(points1 - points2) < 2)
                {
                    continue;
                }

                // Der Match-Tiebreak steht als Satz mit den Punkten da, der
                // Tiebreak eines Satzes als 7:6 mit den Punkten des Unterlegenen.
                sets.Add(inMatchTiebreak
                    ? new SetScore(points1, points2)
                    : points1 > points2
                        ? new SetScore(format.TiebreakAt + 1, format.TiebreakAt, points2)
                        : new SetScore(format.TiebreakAt, format.TiebreakAt + 1, points1));
                games1 = games2 = points1 = points2 = 0;
                inTiebreak = false;
                winner = Decided(sets, format);
                continue;
            }

            var gameTo = e.Kind == LiveEventKind.Game ? e.Side : 0;

            if (gameTo == 0)
            {
                if (e.Side == 1)
                {
                    points1++;
                }
                else
                {
                    points2++;
                }

                if (Math.Max(points1, points2) >= 4 && Math.Abs(points1 - points2) >= 2)
                {
                    gameTo = points1 > points2 ? 1 : 2;
                }
            }

            if (gameTo == 0)
            {
                continue;
            }

            points1 = points2 = 0;

            if (gameTo == 1)
            {
                games1++;
            }
            else
            {
                games2++;
            }

            var high2 = Math.Max(games1, games2);
            var lead = Math.Abs(games1 - games2);
            var at = format.TiebreakAt;

            // Mit Tiebreak endet der Satz bei zwei Vorsprung ab dem Ziel oder mit
            // einem Spiel mehr (7:5); bei Gleichstand am Ziel kommt der Tiebreak.
            // Ohne Tiebreak geht es weiter, bis zwei Spiele Vorsprung stehen.
            var setOver = allowTiebreak
                ? (high2 == at && lead >= 2) || high2 > at
                : high2 >= at && lead >= 2;

            if (setOver)
            {
                sets.Add(new SetScore(games1, games2));
                games1 = games2 = 0;
                winner = Decided(sets, format);
            }
            else if (allowTiebreak && games1 == at && games2 == at)
            {
                inTiebreak = true;
            }
        }

        var finalNow = sets.Count == format.BestOf - 1 && winner is null;
        var matchTiebreakNow = finalNow && format.BestOf > 1 && format.FinalSetMode == FinalSetMode.MatchTiebreak10;

        return new LiveState(sets, games1, games2, points1, points2, inTiebreak, matchTiebreakNow, winner);
    }

    private static int? Decided(IReadOnlyList<SetScore> sets, MatchFormat format)
    {
        if (sets.Count(s => s.WinnerSide == 1) == format.SetsToWin)
        {
            return 1;
        }

        return sets.Count(s => s.WinnerSide == 2) == format.SetsToWin ? 2 : null;
    }
}
