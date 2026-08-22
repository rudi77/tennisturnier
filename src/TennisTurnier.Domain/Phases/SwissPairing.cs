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
    /// Obergrenze für die Schritte einer Rückverfolgung.
    ///
    /// Für Vereinsfelder wird sie nie erreicht — die bevorzugte Paarung passt
    /// fast immer sofort. Sie steht da, damit ein pathologischer Fall abbricht
    /// und nicht als Anfrage endet, die nie zurückkommt.
    ///
    /// Jeder Anlauf bekommt sein eigenes Budget. Teilten sie sich eines, könnte
    /// eine erschöpfte Suche in den Punktgruppen den nächsten Anlauf mitreißen —
    /// und der wäre womöglich der, der eine gültige Paarung gefunden hätte.
    /// </summary>
    private const int SearchBudget = 200_000;

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
    /// Darüber steht die Bedingung, dass sich zwei Spieler nicht zweimal
    /// begegnen. Sie lässt sich nicht durch Sortieren erfüllen, sondern nur
    /// suchend: gefunden wird die Paarung, die der idealen am nächsten kommt und
    /// keine Wiederholung enthält.
    ///
    /// Drei Anläufe, in dieser Reihenfolge:
    /// <list type="number">
    ///   <item>Punktgruppen mit Absteigern, wiederholungsfrei — der Regelfall.</item>
    ///   <item>Das ganze Feld in Tabellenreihenfolge, wiederholungsfrei. Die
    ///     Punktgruppen sind eine Konvention, die Wiederholungsfreiheit eine
    ///     Regel; gehen sie nicht zusammen, weicht die Konvention.</item>
    ///   <item>Das ganze Feld, Wiederholungen erlaubt und nur so viele wie
    ///     nötig.</item>
    /// </list>
    ///
    /// Der dritte Anlauf ist der Grund, warum diese Methode praktisch nie wirft.
    /// Das Verfahren paart jede Runde nach dem Stand von jetzt und schaut nicht
    /// voraus; es kann sich damit selbst in eine Runde manövrieren, für die es
    /// keine wiederholungsfreie Paarung mehr gibt — bei acht Spielern und sieben
    /// Runden in etwa jedem sechsten Verlauf. Dort abzubrechen hieße: das letzte
    /// Ergebnis der vorigen Runde lässt sich nicht mehr eintragen, und das
    /// Turnier steht ohne Vor- und ohne Rückweg. Eine ausgewiesene Wiederholung
    /// ist das kleinere Übel — und sie wird ausgewiesen, nicht verschwiegen.
    /// </summary>
    internal static IReadOnlyList<(Guid Side1, Guid Side2, bool Rematch)> PairRound(
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

        // Der letzte Anlauf lässt Wiederholungen zu und findet deshalb immer eine
        // Paarung: dem Ersten steht dann jeder Übrige als Gegner offen, und das
        // gilt in jedem Schritt der Rückverfolgung. Eine Absage gäbe es hier nur
        // bei ungerader Anzahl — die ist oben schon abgewiesen.
        var pairs =
            ByScoreGroups(standingsOrder, pointsByEntry, previousOpponents)
            ?? Match([.. standingsOrder], previousOpponents, new Budget(SearchBudget), allowRematch: false)
            ?? Match([.. standingsOrder], previousOpponents, new Budget(SearchBudget), allowRematch: true)!;

        return
        [
            .. pairs.Select(pair => (
                pair.Item1,
                pair.Item2,
                previousOpponents.GetValueOrDefault(pair.Item1)?.Contains(pair.Item2) == true)),
        ];
    }

    private static List<(Guid, Guid)>? ByScoreGroups(
        IReadOnlyList<Guid> standingsOrder,
        IReadOnlyDictionary<Guid, int> pointsByEntry,
        IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> previousOpponents)
    {
        var budget = new Budget(SearchBudget);

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
                var matching = Match(stay, previousOpponents, budget, allowRematch: false);

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

        // Die letzte Punktgruppe hat nichts mehr unter sich: sie lässt niemanden
        // durchrutschen, und was hier ankommt, ist gerade. Bliebe jemand übrig,
        // wäre die Schleife oben schon ohne Paarung ausgestiegen.
        return pairs;
    }

    /// <summary>
    /// Sucht eine Paarung einer nach Tabellenstand geordneten Menge.
    ///
    /// Der Erste bekommt den Gegner, der ihm im Dutch-System zusteht: den Ersten
    /// der unteren Hälfte. Geht das nicht, rückt der Gegner in der unteren Hälfte
    /// weiter nach hinten, und erst wenn dort niemand mehr bleibt, kommt die
    /// obere Hälfte in Frage. Jede Wahl wird zurückgenommen, wenn sich der Rest
    /// nicht mehr paaren lässt — sonst scheitert die Runde an ihrem letzten Paar,
    /// obwohl weiter oben eine Alternative lag.
    ///
    /// Mit <paramref name="allowRematch"/> kommen bereits gespielte Paarungen in
    /// Frage — aber erst, nachdem jeder ungespielte Gegner erfolglos war. Damit
    /// entstehen so wenige Wiederholungen wie möglich statt einer beliebigen
    /// Paarung.
    /// </summary>
    private static List<(Guid, Guid)>? Match(
        List<Guid> pool,
        IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> previousOpponents,
        Budget budget,
        bool allowRematch)
    {
        if (pool.Count == 0)
        {
            return [];
        }

        if (!budget.Spend())
        {
            return null;
        }

        var top = pool[0];
        var met = previousOpponents.GetValueOrDefault(top);

        foreach (var index in Candidates(pool, met, allowRematch))
        {
            var rest = new List<Guid>(pool.Count - 2);
            for (var i = 1; i < pool.Count; i++)
            {
                if (i != index)
                {
                    rest.Add(pool[i]);
                }
            }

            if (Match(rest, previousOpponents, budget, allowRematch) is { } tail)
            {
                tail.Insert(0, (top, pool[index]));
                return tail;
            }
        }

        return null;
    }

    /// <summary>
    /// Die Gegner des Ersten in der Reihenfolge, in der sie in Frage kommen:
    /// zuerst alle ungespielten nach Dutch-Vorzug, dann — nur wenn Wiederholungen
    /// erlaubt sind — die bereits gespielten in derselben Reihenfolge.
    /// </summary>
    private static IEnumerable<int> Candidates(
        List<Guid> pool,
        IReadOnlySet<Guid>? met,
        bool allowRematch)
    {
        foreach (var index in Preferences(pool.Count))
        {
            if (met?.Contains(pool[index]) != true)
            {
                yield return index;
            }
        }

        if (!allowRematch || met is null)
        {
            yield break;
        }

        foreach (var index in Preferences(pool.Count))
        {
            if (met.Contains(pool[index]))
            {
                yield return index;
            }
        }
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

    /// <summary>
    /// Eine erschöpfte Suche gibt auf, sie wirft nicht: der nächste Anlauf soll
    /// noch zum Zug kommen. Geworfen wird erst, wenn auch der letzte nichts
    /// findet — und der letzte lässt Wiederholungen zu und braucht deshalb keine
    /// Rückverfolgung.
    /// </summary>
    private sealed class Budget(int limit)
    {
        private int _left = limit;

        internal bool Spend() => --_left >= 0;
    }
}
