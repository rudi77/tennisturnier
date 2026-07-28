using TennisTurnier.Domain.Formats;

namespace TennisTurnier.Domain.Phases;

/// <summary>
/// Die Bilanz eines Teilnehmers innerhalb einer Gruppe — die Grundlage jeder
/// Tabelle und jedes Tiebreakers.
/// </summary>
public sealed record TableRecord(
    Guid EntryId,
    string DisplayName,
    string? Group,
    int? Seed,
    int Played,
    int Won,
    int Lost,
    int Points,
    int SetsWon,
    int SetsLost,
    int GamesWon,
    int GamesLost)
{
    public Standing ToStanding(int rank) => new(
        rank, EntryId, DisplayName, Group, Played, Won, Lost, Points, SetsWon, SetsLost, GamesWon, GamesLost);
}

/// <summary>
/// Was ein Tiebreaker über die Begegnungen wissen muss.
///
/// <see cref="Buchholz"/> steht hier und nicht im <see cref="TableRecord"/>, weil
/// es keine Eigenschaft eines Teilnehmers ist, sondern eine seiner Gegner: es
/// ändert sich, wenn jemand ganz anderes gewinnt.
/// </summary>
public sealed record TiebreakContext(
    IReadOnlyDictionary<(Guid Winner, Guid Loser), int> HeadToHead,
    IReadOnlyDictionary<Guid, int> Buchholz);

/// <summary>
/// Löst Punktgleichheit auf, in der Reihenfolge aus der Phasendefinition.
///
/// Die Reihenfolge kommt bewusst aus der Definition und nicht aus dem Code:
/// welches Kriterium zuerst greift, ist eine Festlegung der Ausschreibung und
/// unterscheidet sich von Turnier zu Turnier (ADR-0001).
/// </summary>
public static class TiebreakerChain
{
    /// <summary>
    /// Ordnet eine Gruppe punktgleicher Teilnehmer.
    ///
    /// Der direkte Vergleich wird ausdrücklich nur innerhalb der punktgleichen
    /// Teilmenge gerechnet. Bei drei punktgleichen Teilnehmern entsteht sonst
    /// leicht ein Ringschluss — A schlägt B, B schlägt C, C schlägt A —, und wer
    /// dabei die Begegnungen gegen Außenstehende mitzählt, bekommt eine
    /// Reihenfolge, die niemand nachrechnen kann.
    /// </summary>
    public static IReadOnlyList<TableRecord> Order(
        IReadOnlyList<TableRecord> tied,
        IReadOnlyList<Tiebreaker> tiebreakers,
        TiebreakContext context)
    {
        ArgumentNullException.ThrowIfNull(tied);
        ArgumentNullException.ThrowIfNull(tiebreakers);
        ArgumentNullException.ThrowIfNull(context);

        if (tied.Count <= 1)
        {
            return tied;
        }

        var members = tied.Select(record => record.EntryId).ToHashSet();

        return
        [
            .. tied
                .OrderByDescending(record => Key(record, tiebreakers, context, members), Lexicographic)
                // Ohne Losentscheid bleibt die Reihenfolge sonst der
                // Aufzählungsreihenfolge überlassen und wechselte bei jedem Abruf.
                .ThenBy(record => record.Seed ?? int.MaxValue)
                .ThenBy(record => record.DisplayName, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Ein Vergleichsschlüssel je Teilnehmer: ein Wert pro Kriterium, in der
    /// Reihenfolge der Kette. Das ist derselbe Vergleich, den ein Mensch
    /// anstellt — erst Kriterium eins, bei Gleichstand Kriterium zwei.
    /// </summary>
    private static IReadOnlyList<int> Key(
        TableRecord record,
        IReadOnlyList<Tiebreaker> tiebreakers,
        TiebreakContext context,
        IReadOnlySet<Guid> members) =>
        [.. tiebreakers.Select(tiebreaker => Value(record, tiebreaker, context, members))];

    private static int Value(
        TableRecord record,
        Tiebreaker tiebreaker,
        TiebreakContext context,
        IReadOnlySet<Guid> members) => tiebreaker switch
    {
        Tiebreaker.DirectEncounter => DirectEncounter(record, context, members),
        Tiebreaker.SetRatio => record.SetsWon - record.SetsLost,
        Tiebreaker.GameRatio => record.GamesWon - record.GamesLost,
        Tiebreaker.Buchholz => context.Buchholz.GetValueOrDefault(record.EntryId),

        // Der Losentscheid entscheidet nichts, was hier zu entscheiden wäre: er
        // wird erst gebraucht, wenn alles andere gleich ist, und dann ordnet die
        // stabile Nachsortierung nach Setzung und Name. Ein echtes Los wäre bei
        // jedem Abruf ein anderes — die Tabelle würde bei jedem Neuladen tanzen.
        _ => 0,
    };

    /// <summary>
    /// Siege minus Niederlagen aus den Begegnungen der punktgleichen Teilnehmer
    /// untereinander.
    /// </summary>
    private static int DirectEncounter(
        TableRecord record,
        TiebreakContext context,
        IReadOnlySet<Guid> members)
    {
        var balance = 0;

        foreach (var ((winner, loser), count) in context.HeadToHead)
        {
            if (!members.Contains(winner) || !members.Contains(loser))
            {
                continue;
            }

            if (winner == record.EntryId)
            {
                balance += count;
            }
            else if (loser == record.EntryId)
            {
                balance -= count;
            }
        }

        return balance;
    }

    private static readonly IComparer<IReadOnlyList<int>> Lexicographic = new LexicographicComparer();

    private sealed class LexicographicComparer : IComparer<IReadOnlyList<int>>
    {
        public int Compare(IReadOnlyList<int>? x, IReadOnlyList<int>? y)
        {
            if (x is null || y is null)
            {
                return x is null && y is null ? 0 : x is null ? -1 : 1;
            }

            for (var index = 0; index < Math.Min(x.Count, y.Count); index++)
            {
                var comparison = x[index].CompareTo(y[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return x.Count.CompareTo(y.Count);
        }
    }
}
