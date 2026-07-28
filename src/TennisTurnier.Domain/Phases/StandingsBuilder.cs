using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Phases;

/// <summary>
/// Die Tabellenrechnung, die sich Gruppenphase und Schweizer System teilen.
///
/// Beide zählen dieselbe Bilanz und lösen Punktgleichheit mit derselben Kette
/// auf; sie unterscheiden sich nur darin, wie sie zu ihren Paarungen kommen. Die
/// Rechnung zweimal zu schreiben hieße, zwei Tabellen zu haben, die bei der
/// nächsten Änderung auseinanderlaufen — und die Qualifikation für eine
/// Folgephase hängt an ihr.
/// </summary>
internal static class StandingsBuilder
{
    /// <summary>
    /// Die Bilanz eines Teilnehmers aus allen Matches, in denen er steht.
    ///
    /// Ein Freilos zählt als Sieg samt Punkten, aber ohne Sätze und Spiele. Im
    /// Schweizer System ist das der Sinn der Sache: wer bei ungerader
    /// Teilnehmerzahl aussetzt, soll dadurch nicht zurückfallen. In einer
    /// Gruppenphase kommt der Fall nicht vor — die Kreismethode lässt bei
    /// ungerader Gruppengröße einen aussetzen, ohne ein Match anzulegen.
    /// </summary>
    internal static TableRecord Accumulate(SeededEntry entry, string? group, PhaseState state)
    {
        int played = 0, won = 0, lost = 0, points = 0;
        int setsWon = 0, setsLost = 0, gamesWon = 0, gamesLost = 0;

        foreach (var match in state.Matches)
        {
            if (match.Score is not { } score)
            {
                continue;
            }

            var side = SideOf(match, entry.EntryId);
            if (side is not { } mine)
            {
                continue;
            }

            played++;

            if (score.Outcome == MatchOutcome.Bye)
            {
                won++;
                points += state.Definition.Scoring.Win;
                continue;
            }

            var other = mine == 1 ? 2 : 1;

            setsWon += score.SetsWonBy(mine);
            setsLost += score.SetsWonBy(other);
            gamesWon += score.GamesWonBy(mine);
            gamesLost += score.GamesWonBy(other);

            if (score.WinnerSide == mine)
            {
                won++;
                points += score.Outcome == MatchOutcome.Walkover
                    ? state.Definition.Scoring.Walkover
                    : state.Definition.Scoring.Win;
            }
            else
            {
                lost++;
                points += state.Definition.Scoring.Loss;
            }
        }

        return new TableRecord(
            entry.EntryId,
            entry.DisplayName,
            group,
            entry.Seed,
            played,
            won,
            lost,
            points,
            setsWon,
            setsLost,
            gamesWon,
            gamesLost);
    }

    internal static int? SideOf(Match match, Guid entryId) =>
        match.Side1.EntryId == entryId ? 1
        : match.Side2.EntryId == entryId ? 2
        : null;

    /// <summary>
    /// Sortiert eine Tabelle: erst Punkte, dann die Tiebreaker-Kette innerhalb
    /// jeder punktgleichen Gruppe.
    /// </summary>
    internal static IEnumerable<Standing> Rank(
        IReadOnlyList<TableRecord> table,
        IReadOnlyList<Tiebreaker> tiebreakers,
        TiebreakContext context)
    {
        var rank = 0;

        foreach (var group in table.GroupBy(record => record.Points).OrderByDescending(g => g.Key))
        {
            foreach (var record in TiebreakerChain.Order([.. group], tiebreakers, context))
            {
                yield return record.ToStanding(++rank);
            }
        }
    }

    /// <summary>Der Tiebreak-Kontext zu einer ganzen Phase.</summary>
    internal static TiebreakContext ContextOf(PhaseState state)
    {
        var records = state.Entries
            .Where(entry => entry.IsSettled)
            .Select(entry => Accumulate(entry, null, state))
            .ToList();

        return ContextOf(records, state.Matches);
    }

    /// <summary>
    /// Direkter Vergleich und Buchholz zu einer bereits gerechneten Tabelle.
    ///
    /// Buchholz ist die Summe der <em>Punkte</em> aller Gegner, nicht die ihrer
    /// Siege: ein kampflos gewonnenes Match zählt in der Tabelle nach dem
    /// Punktesystem der Ausschreibung, und ein Gegner, der so weitergekommen ist,
    /// darf hier nicht mehr wiegen als dort. Wer keinen Gegner hatte, hat
    /// Buchholz null — deshalb kommen die Schlüssel aus der Tabelle und nicht aus
    /// den Begegnungen.
    /// </summary>
    internal static TiebreakContext ContextOf(
        IReadOnlyList<TableRecord> records,
        IReadOnlyList<Match> matches)
    {
        var headToHead = new Dictionary<(Guid Winner, Guid Loser), int>();
        var opponents = new Dictionary<Guid, List<Guid>>();

        foreach (var match in matches)
        {
            if (match.Score is not { } score
                || score.Outcome == MatchOutcome.Bye
                || match.WinnerEntryId is not { } winner
                || match.LoserEntryId is not { } loser)
            {
                continue;
            }

            headToHead[(winner, loser)] = headToHead.GetValueOrDefault((winner, loser)) + 1;

            Opponents(opponents, winner).Add(loser);
            Opponents(opponents, loser).Add(winner);
        }

        var pointsByEntry = records.ToDictionary(record => record.EntryId, record => record.Points);

        var buchholz = records.ToDictionary(
            record => record.EntryId,
            record => opponents.GetValueOrDefault(record.EntryId, [])
                .Sum(opponent => pointsByEntry.GetValueOrDefault(opponent)));

        return new TiebreakContext(headToHead, buchholz);
    }

    private static List<Guid> Opponents(Dictionary<Guid, List<Guid>> map, Guid entryId)
    {
        if (!map.TryGetValue(entryId, out var list))
        {
            list = [];
            map[entryId] = list;
        }

        return list;
    }
}
