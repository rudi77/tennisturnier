using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Phases;

/// <summary>
/// Die Paarung einer Runde im Schweizer System, als reine Rechnung.
///
/// Sie steht für sich, weil sie die einzige Stelle im System ist, an der ein
/// Suchverfahren läuft: eine wiederholungsfreie Paarung ist keine Formel,
/// sondern ein Matching-Problem, und ein Verfahren, das rät und zurücknimmt,
/// gehört nicht in dieselbe Klasse wie das Zählen einer Tabelle.
/// </summary>
internal static class SwissPairing
{
    /// <summary>
    /// Obergrenze für die Schritte der Rückverfolgung.
    ///
    /// Für Vereinsfelder wird sie nie erreicht — die bevorzugte Paarung passt
    /// fast immer sofort. Sie steht da, damit ein pathologischer Fall als klare
    /// Absage endet und nicht als Anfrage, die nie zurückkommt.
    /// </summary>
    private const int SearchBudget = 500_000;

    /// <summary>
    /// Wer bei ungerader Teilnehmerzahl aussetzt: der Letzte der Tabelle, der
    /// noch kein Freilos hatte.
    ///
    /// Höchstens einmal pro Spieler — ein zweites Freilos wäre ein zweiter
    /// geschenkter Punkt, und der entscheidet ein Turnier, in dem alle anderen
    /// ihn erspielen müssen. Unten in der Tabelle, weil oben um den Turniersieg
    /// gespielt wird und ein geschenkter Punkt dort am meisten wiegt.
    /// </summary>
    internal static Guid? PickBye(IReadOnlyList<Guid> standingsOrder, IReadOnlySet<Guid> alreadyHadBye)
    {
        ArgumentNullException.ThrowIfNull(standingsOrder);
        ArgumentNullException.ThrowIfNull(alreadyHadBye);

        if (standingsOrder.Count % 2 == 0)
        {
            return null;
        }

        for (var index = standingsOrder.Count - 1; index >= 0; index--)
        {
            if (!alreadyHadBye.Contains(standingsOrder[index]))
            {
                return standingsOrder[index];
            }
        }

        throw new DomainException(
            "Für diese Runde bleibt kein Spieler übrig, der noch kein Freilos hatte. " +
            "Bei ungerader Teilnehmerzahl sind höchstens so viele Runden spielbar, wie es Spieler gibt.");
    }

    /// <summary>
    /// Paart eine Runde nach dem Dutch-System: die Tabelle wird in Punktgruppen
    /// zerlegt, jede Punktgruppe in obere und untere Hälfte geteilt und über
    /// Kreuz gepaart — der Erste der oberen Hälfte gegen den Ersten der unteren.
    ///
    /// Bleibt in einer Punktgruppe jemand übrig, steigt er in die nächste ab
    /// („Floater"). Abgestiegen wird von unten: wer in seiner Punktgruppe hinten
    /// steht, trifft auf die nächstschwächere, nicht umgekehrt.
    ///
    /// Über allem steht die harte Bedingung, dass sich zwei Spieler nicht zweimal
    /// begegnen. Sie lässt sich nicht durch Sortieren erfüllen, sondern nur
    /// suchend: gefunden wird die Paarung, die der idealen am nächsten kommt und
    /// keine Wiederholung enthält.
    /// </summary>
    internal static IReadOnlyList<(Guid Side1, Guid Side2)> PairRound(
        IReadOnlyList<Guid> standingsOrder,
        IReadOnlyDictionary<Guid, int> pointsByEntry,
        IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> previousOpponents)
    {
        ArgumentNullException.ThrowIfNull(standingsOrder);
        ArgumentNullException.ThrowIfNull(pointsByEntry);
        ArgumentNullException.ThrowIfNull(previousOpponents);

        if (standingsOrder.Count % 2 != 0)
        {
            throw new DomainException(
                $"Eine Runde paart eine gerade Anzahl Spieler, waren {standingsOrder.Count}. " +
                "Das Freilos wird vorher vergeben.");
        }

        var budget = new Budget(SearchBudget);

        return ByScoreGroups(standingsOrder, pointsByEntry, previousOpponents, budget)
            // Die Punktgruppen sind eine Konvention, die Wiederholungsfreiheit
            // eine Regel. Geht beides nicht zusammen, gilt die Regel: gesucht
            // wird dann über das ganze Feld, in der Reihenfolge der Tabelle.
            ?? Match([.. standingsOrder], previousOpponents, budget)
            ?? throw new DomainException(
                "Für diese Runde gibt es keine Paarung mehr, in der nicht mindestens zwei Spieler " +
                "ein zweites Mal aufeinandertreffen. Das Feld ist für so viele Runden zu klein.");
    }

    private static List<(Guid, Guid)>? ByScoreGroups(
        IReadOnlyList<Guid> standingsOrder,
        IReadOnlyDictionary<Guid, int> pointsByEntry,
        IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> previousOpponents,
        Budget budget)
    {
        var groups = standingsOrder
            .GroupBy(id => pointsByEntry.GetValueOrDefault(id))
            .OrderByDescending(group => group.Key)
            .Select(group => group.ToList())
            .ToList();

        var pairs = new List<(Guid, Guid)>();
        var carried = new List<Guid>();

        for (var index = 0; index < groups.Count; index++)
        {
            var pool = new List<Guid>(carried);
            pool.AddRange(groups[index]);
            carried.Clear();

            // Die letzte Punktgruppe hat nichts mehr unter sich. Was hier nicht
            // gepaart wird, bleibt ohne Gegner.
            var maxFloaters = index == groups.Count - 1 ? 0 : pool.Count;
            var matched = false;

            for (var floaters = pool.Count % 2; floaters <= maxFloaters && !matched; floaters += 2)
            {
                var stay = pool.Take(pool.Count - floaters).ToList();
                var matching = Match(stay, previousOpponents, budget);

                if (matching is null)
                {
                    continue;
                }

                pairs.AddRange(matching);
                carried.AddRange(pool.Skip(pool.Count - floaters));
                matched = true;
            }

            if (!matched)
            {
                return null;
            }
        }

        return carried.Count == 0 ? pairs : null;
    }

    /// <summary>
    /// Sucht eine wiederholungsfreie Paarung einer nach Tabellenstand geordneten
    /// Menge.
    ///
    /// Der Erste bekommt den Gegner, der ihm im Dutch-System zusteht: den Ersten
    /// der unteren Hälfte. Geht das nicht, rückt der Gegner in der unteren Hälfte
    /// weiter nach hinten, und erst wenn dort niemand mehr bleibt, kommt die
    /// obere Hälfte in Frage. Jede Wahl wird zurückgenommen, wenn sich der Rest
    /// nicht mehr paaren lässt — sonst scheitert die Runde an ihrem letzten Paar,
    /// obwohl weiter oben eine Alternative lag.
    /// </summary>
    private static List<(Guid, Guid)>? Match(
        List<Guid> pool,
        IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> previousOpponents,
        Budget budget)
    {
        if (pool.Count == 0)
        {
            return [];
        }

        if (pool.Count % 2 != 0)
        {
            return null;
        }

        budget.Spend();

        var top = pool[0];
        var met = previousOpponents.GetValueOrDefault(top);

        foreach (var index in Preferences(pool.Count))
        {
            var opponent = pool[index];

            if (met is not null && met.Contains(opponent))
            {
                continue;
            }

            var rest = new List<Guid>(pool.Count - 2);
            for (var i = 1; i < pool.Count; i++)
            {
                if (i != index)
                {
                    rest.Add(pool[i]);
                }
            }

            if (Match(rest, previousOpponents, budget) is { } tail)
            {
                tail.Insert(0, (top, opponent));
                return tail;
            }
        }

        return null;
    }

    /// <summary>
    /// Die Gegner des Tabellenersten in der Reihenfolge, in der sie in Frage
    /// kommen: zuerst der Erste der unteren Hälfte, dann die Folgenden dort, und
    /// zuletzt — nur wenn die untere Hälfte durch ist — die obere.
    /// </summary>
    private static IEnumerable<int> Preferences(int count)
    {
        var half = count / 2;

        for (var index = half; index < count; index++)
        {
            yield return index;
        }

        for (var index = half - 1; index >= 1; index--)
        {
            yield return index;
        }
    }

    private sealed class Budget(int limit)
    {
        private int _left = limit;

        internal void Spend()
        {
            if (--_left < 0)
            {
                throw new DomainException(
                    "Die Suche nach einer wiederholungsfreien Paarung findet kein Ende. " +
                    "Das Feld ist für so viele Runden zu klein.");
            }
        }
    }
}
