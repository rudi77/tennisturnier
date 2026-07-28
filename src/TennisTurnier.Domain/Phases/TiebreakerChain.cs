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

        return Resolve(tied, tiebreakers, 0, context);
    }

    /// <summary>
    /// Wendet ein Kriterium an und löst jede verbleibende Gleichheit mit dem
    /// nächsten auf — jeweils nur unter denen, die noch gleichauf sind.
    ///
    /// Die Einschränkung ist der Kern. Trennt das Satzverhältnis einen von drei
    /// Punktgleichen heraus, ist der direkte Vergleich der beiden übrigen eine
    /// andere Rechnung als der über alle drei: im Ringschluss A schlägt B, B
    /// schlägt C, C schlägt A haben alle drei die Bilanz null, zwei davon
    /// betrachtet hat einer gewonnen. Über die ganze Menge gerechnet stünde der
    /// Sieger dieser Begegnung hinter dem Verlierer — genau die Reihenfolge, die
    /// niemand nachrechnen kann.
    /// </summary>
    private static IReadOnlyList<TableRecord> Resolve(
        IReadOnlyList<TableRecord> tied,
        IReadOnlyList<Tiebreaker> tiebreakers,
        int depth,
        TiebreakContext context)
    {
        if (tied.Count <= 1)
        {
            return tied;
        }

        if (depth >= tiebreakers.Count)
        {
            // Alle Kriterien erschöpft. Ein echtes Los wäre bei jedem Abruf ein
            // anderes — die Tabelle tanzte bei jedem Neuladen. Entschieden wird
            // deshalb stabil nach Setzung, dann nach Name.
            return
            [
                .. tied
                    .OrderBy(record => record.Seed ?? int.MaxValue)
                    .ThenBy(record => record.DisplayName, StringComparer.Ordinal),
            ];
        }

        var members = tied.Select(record => record.EntryId).ToHashSet();

        return
        [
            .. tied
                .GroupBy(record => Value(record, tiebreakers[depth], context, members))
                .OrderByDescending(group => group.Key)
                .SelectMany(group => Resolve([.. group], tiebreakers, depth + 1, context)),
        ];
    }

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

        // Der Losentscheid trennt niemanden: er greift erst, wenn alles andere
        // gleich ist, und dann ordnet die stabile Nachsortierung.
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
}
