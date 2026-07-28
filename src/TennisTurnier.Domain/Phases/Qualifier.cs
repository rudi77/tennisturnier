using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Phases;

/// <summary>
/// Wer aus einer Vorphase weiterkommt — und auf welchem Platz der Folgephase.
///
/// Getrennt von den Formaten, weil es keine Frage des Paarungsverfahrens ist:
/// dieselbe Regel „die besten zwei jeder Gruppe" gilt für eine Gruppenphase
/// ebenso wie für ein Schweizer System davor (ADR-0001).
/// </summary>
public static class Qualifier
{
    /// <summary>
    /// Die Startplätze der Folgephase, bevor jemand feststeht.
    ///
    /// Sie tragen als Herkunft einen Gruppenplatz — „Erster der Gruppe A" —, und
    /// genau daraus entsteht das Bracket der Endrunde, während die Gruppen noch
    /// laufen. Aufgelöst werden sie später über
    /// <see cref="Phase.ResolveGroupPositions"/>, mit demselben Mechanismus wie
    /// „Sieger aus Halbfinale 1".
    /// </summary>
    public static IReadOnlyList<SeededEntry> Slots(
        Qualification qualification,
        Guid sourcePhaseId,
        PhaseDefinition sourceDefinition,
        IReadOnlyList<SeededEntry> sourceEntries)
    {
        ArgumentNullException.ThrowIfNull(qualification);
        ArgumentNullException.ThrowIfNull(sourceDefinition);
        ArgumentNullException.ThrowIfNull(sourceEntries);

        var positions = Positions(qualification, sourceDefinition, sourceEntries);

        return
        [
            .. positions.Select((position, index) =>
            {
                var origin = ParticipantRef.FromGroupPosition(sourcePhaseId, position.Group, position.Rank);

                return SeededEntry.ForSlot(origin, seed: index + 1, label: origin.ToString());
            }),
        ];
    }

    /// <summary>
    /// Ordnet den Startplätzen ihre tatsächlichen Meldungen zu, sobald die
    /// Vorphase steht — Gruppenplätze wie Auffüllplätze.
    /// </summary>
    public static IReadOnlyDictionary<(string Group, int Rank), Guid> Resolve(
        PhaseState sourceState,
        Standings standings)
    {
        ArgumentNullException.ThrowIfNull(sourceState);
        ArgumentNullException.ThrowIfNull(standings);

        var resolved = new Dictionary<(string, int), Guid>();

        foreach (var group in standings.Groups)
        {
            var rank = 0;

            foreach (var place in standings.InGroup(group))
            {
                resolved[(group ?? string.Empty, ++rank)] = place.EntryId;
            }
        }

        // Die Auffüllplätze vergleichen dieselben Ränge über alle Gruppen hinweg
        // und brauchen deshalb einen eigenen Durchgang. Ohne obere Schranke liefe
        // er über Ränge, die es nirgends gibt.
        var deepest = standings.Groups
            .Select(group => standings.InGroup(group).Count())
            .DefaultIfEmpty(0)
            .Max();

        var context = RoundRobinFormat.BuildContext(sourceState);

        for (var rank = 1; rank <= deepest; rank++)
        {
            foreach (var (key, entryId) in
                     BestOfRank(standings, rank, sourceState.Definition.Tiebreakers, context))
            {
                resolved[key] = entryId;
            }
        }

        return resolved;
    }

    /// <summary>
    /// Welche Gruppenplätze weiterkommen, in der Reihenfolge der Setzung für die
    /// Folgephase.
    ///
    /// Bei <see cref="SeedingRule.CrossGroup"/> stehen zuerst alle Gruppensieger,
    /// dann alle Zweiten. In Verbindung mit der Standard-Setzlistenverteilung des
    /// K.-o.-Baums trifft damit jeder Gruppensieger auf den Zweiten einer anderen
    /// Gruppe — bei acht Qualifikanten spielt Setzung 1 (Sieger A) gegen Setzung
    /// 8 (Zweiter D), Setzung 4 (Sieger D) gegen Setzung 5 (Zweiter A) und so
    /// fort.
    /// </summary>
    private static IReadOnlyList<(string Group, int Rank)> Positions(
        Qualification qualification,
        PhaseDefinition sourceDefinition,
        IReadOnlyList<SeededEntry> sourceEntries)
    {
        // Die tatsächlichen Gruppengrößen, nicht die gerechneten: bei fünf
        // Teilnehmern in zwei Gruppen hat eine drei und eine zwei, und ein Platz
        // „Dritter der Gruppe B" bliebe für immer unbesetzt.
        var groups = RoundRobinFormat
            .SplitIntoGroups(sourceEntries, sourceDefinition.GroupCount)
            .Select(group => (group.Name, Size: group.Members.Count))
            .ToList();

        var perGroup = qualification.Rule == QualificationRule.All
            ? groups.Max(group => group.Size)
            : qualification.N;

        if (perGroup < 1)
        {
            throw new DomainException(
                $"Aus einer Vorphase mit {sourceEntries.Count} Teilnehmern kommt niemand weiter.");
        }

        var positions = new List<(string Group, int Rank)>();

        for (var rank = 1; rank <= perGroup; rank++)
        {
            positions.AddRange(OrderForRank(groups, rank)
                .Where(group => group.Size >= rank)
                .Select(group => (group.Name, rank)));
        }

        if (qualification.Rule == QualificationRule.BestThirds)
        {
            var candidates = groups.Count(group => group.Size > perGroup);

            positions.AddRange(BestThirds(candidates, perGroup, positions.Count));
        }

        return positions;
    }

    /// <summary>
    /// Innerhalb eines Rangs gehen die Gruppen der Reihe nach.
    ///
    /// Das ist kein Verzicht auf die Überkreuzung, sondern genau sie: die
    /// Setzlistenverteilung des K.-o.-Baums paart Setzung <c>i</c> mit Setzung
    /// <c>2G+1−i</c>. Der Sieger der Gruppe <c>g</c> trägt Setzung <c>g+1</c> und
    /// trifft damit auf den Zweiten der Gruppe <c>G−1−g</c> — bei vier Gruppen
    /// also A1 gegen D2, B1 gegen C2, C1 gegen B2, D1 gegen A2.
    ///
    /// Bei ungerader Gruppenzahl geht die Rechnung für genau eine Gruppe nicht
    /// auf: <c>g = (G−1)/2</c> träfe auf sich selbst. Deren Zweiter tauscht
    /// deshalb den Platz mit dem folgenden. Bei drei oder mehr
    /// Qualifikationsrängen kann in einem ungeraden Feld dennoch eine
    /// Wiederholung entstehen — sie ganz auszuschließen hieße, die Überkreuzung
    /// an anderer Stelle aufzugeben.
    /// </summary>
    private static IEnumerable<(string Name, int Size)> OrderForRank(
        IReadOnlyList<(string Name, int Size)> groups,
        int rank)
    {
        var ordered = groups.ToList();
        var middle = (ordered.Count - 1) / 2;

        if (rank > 1 && ordered.Count % 2 == 1 && ordered.Count > 1)
        {
            (ordered[middle], ordered[middle + 1]) = (ordered[middle + 1], ordered[middle]);
        }

        return ordered;
    }

    /// <summary>
    /// Füllt das Feld mit den besten Nächstplatzierten auf die nächste
    /// Zweierpotenz auf — der Fall „sechs Gruppen, die besten Zwei plus die vier
    /// besten Dritten".
    ///
    /// Welche Gruppen das sind, steht vorher nicht fest; die Plätze tragen
    /// deshalb den Rang und keine Gruppe. Aufgelöst werden sie über den Vergleich
    /// der Tabellen (siehe <see cref="ResolveBestOfRank"/>).
    /// </summary>
    private static IEnumerable<(string Group, int Rank)> BestThirds(
        int candidates,
        int perGroup,
        int qualified)
    {
        var target = KnockoutFormat.NextPowerOfTwo(qualified);

        // Begrenzt durch die Zahl der Gruppen, die diesen Rang überhaupt haben.
        // Bei ungleich großen Gruppen entstünden sonst Plätze, für die es keinen
        // Kandidaten gibt — und das Match dahinter bliebe für immer offen.
        var missing = Math.Min(target - qualified, candidates);

        return Enumerable.Range(1, missing).Select(index => (BestOfRankGroup(index), perGroup + 1));
    }

    /// <summary>
    /// Der Gruppenname eines Auffüllplatzes. Er benennt keine Gruppe, sondern die
    /// Reihenfolge unter den Nächstplatzierten: „bester Dritter", „zweitbester
    /// Dritter".
    /// </summary>
    internal static string BestOfRankGroup(int index) => index switch
    {
        1 => "#bester",
        2 => "#zweitbester",
        3 => "#drittbester",
        _ => $"#{index}.-bester",
    };

    internal static bool IsBestOfRank(string group) => group.StartsWith('#');

    /// <summary>
    /// Ordnet die Auffüllplätze eines Rangs zu: alle Teilnehmer dieses Rangs aus
    /// allen Gruppen, verglichen über dieselbe Tiebreaker-Kette, die auch die
    /// Gruppentabellen ordnet. Ein Auffüllplatz soll nach denselben Maßstäben
    /// vergeben werden wie ein direkter Gruppenplatz.
    /// </summary>
    private static IReadOnlyDictionary<(string Group, int Rank), Guid> BestOfRank(
        Standings standings,
        int rank,
        IReadOnlyList<Tiebreaker> tiebreakers,
        TiebreakContext context)
    {
        var candidates = standings.Groups
            .Select(group => standings.InGroup(group).Skip(rank - 1).FirstOrDefault())
            .Where(place => place is not null)
            .Select(place => place!)
            .ToList();

        var records = candidates
            .Select(place => new TableRecord(
                place.EntryId, place.DisplayName, place.Group, Seed: null,
                place.Played, place.Won, place.Lost, place.Points,
                place.SetsWon, place.SetsLost, place.GamesWon, place.GamesLost))
            .ToList();

        // Nach Punkten und dann derselben Kette wie in der Gruppe — ein
        // Auffüllplatz soll nach denselben Maßstäben vergeben werden wie ein
        // direkter Gruppenplatz.
        var ordered = records
            .GroupBy(record => record.Points)
            .OrderByDescending(group => group.Key)
            .SelectMany(group => TiebreakerChain.Order([.. group], tiebreakers, context))
            .ToList();

        return ordered
            .Select((record, index) => (Key: (BestOfRankGroup(index + 1), rank), record.EntryId))
            .ToDictionary(pair => pair.Key, pair => pair.EntryId);
    }

}
