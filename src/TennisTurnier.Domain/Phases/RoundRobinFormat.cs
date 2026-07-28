using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Phases;

/// <summary>
/// Jeder gegen jeden — als Liga über ein Feld oder als Gruppenphase über
/// mehrere.
///
/// Anders als im K.-o.-System stehen alle Paarungen von Anfang an fest; es gibt
/// nichts aufzulösen. Die eigentliche Arbeit steckt deshalb nicht im Erzeugen
/// der Paarungen, sondern in der Tabelle — und dort in den Tiebreakern.
/// </summary>
public sealed class RoundRobinFormat : IPhaseFormat
{
    public PhaseFormatKind Kind => PhaseFormatKind.RoundRobin;

    public IReadOnlyList<Pairing> GeneratePairings(PhaseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Matches.Count > 0)
        {
            return [];
        }

        var groups = SplitIntoGroups(state.Entries, state.Definition.GroupCount);
        RequirePlayableGroups(groups, state.Entries.Count, state.Definition.GroupCount);

        var pairings = new List<Pairing>();

        foreach (var (name, members) in groups)
        {
            AddGroupPairings(members, name, state.Definition, pairings);
        }

        return pairings;
    }

    public bool IsComplete(PhaseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.Matches.Count > 0 && state.Matches.All(m => m.Status == MatchStatus.Finished);
    }

    public Standings ComputeStandings(PhaseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Nur besetzte Plätze: solange eine Vorphase läuft, sind die Startplätze
        // dieser Phase bloß Gruppenplätze. Eine Tabelle mit Zeilen, hinter denen
        // niemand steht, wäre schlimmer als eine leere.
        var settled = state.Entries.Where(entry => entry.IsSettled).ToList();
        var groups = GroupsFromMatches(settled, state.Matches);
        var context = BuildContext(state);
        var places = new List<Standing>();

        foreach (var (name, members) in groups)
        {
            var table = members
                .Select(entry => Accumulate(entry, name, state, groups.Count > 1))
                .ToList();

            places.AddRange(Rank(table, state.Definition.Tiebreakers, context));
        }

        return new Standings(places);
    }

    /// <summary>
    /// Weist eine Auslosung zurück, bei der eine Gruppe allein bliebe.
    ///
    /// Sie ginge sonst wortlos durch: die betroffene Gruppe bekäme kein einziges
    /// Match, ihre Teilnehmer schieden ohne ein Spiel aus, und die Endrunde
    /// bekäme Plätze, die niemand einnehmen kann — das Turnier ließe sich nie
    /// abschließen. Besser eine klare Absage vor der Auslosung als ein Turnier,
    /// das erst am Spieltag stillsteht.
    /// </summary>
    private static void RequirePlayableGroups(
        IReadOnlyList<(string Name, IReadOnlyList<SeededEntry> Members)> groups,
        int participants,
        int groupCount)
    {
        var lonely = groups.Where(group => group.Members.Count < 2).ToList();
        if (lonely.Count == 0)
        {
            return;
        }

        throw new DomainException(
            $"{groupCount} Gruppen brauchen mindestens {groupCount * 2} Teilnehmer, es sind {participants}. " +
            $"Ohne Gegner bliebe{(lonely.Count == 1 ? "" : "n")} " +
            $"{string.Join(", ", lonely.Select(group => group.Name.Length == 0 ? "die Gruppe" : group.Name))}.");
    }

    /// <summary>
    /// Wer in welcher Gruppe steht — abgelesen an den Matches, nicht neu
    /// gerechnet.
    ///
    /// Die Einteilung entstand einmal beim Auslosen und steht seither an jedem
    /// Match. Sie hier erneut aus der Setzung herzuleiten hieße, sie ein zweites
    /// Mal zu bestimmen: sobald die übergebene Menge oder ein Anzeigename
    /// abweicht, fällt jemand in eine Gruppe, in der er nie gespielt hat — und
    /// die Qualifikation für die Endrunde wird genau daraus abgeleitet.
    /// </summary>
    private static IReadOnlyList<(string Name, IReadOnlyList<SeededEntry> Members)> GroupsFromMatches(
        IReadOnlyList<SeededEntry> entries,
        IReadOnlyList<Match> matches)
    {
        var groupByEntry = new Dictionary<Guid, string>();

        foreach (var match in matches)
        {
            foreach (var entryId in new[] { match.Side1.EntryId, match.Side2.EntryId })
            {
                if (entryId is { } id)
                {
                    groupByEntry[id] = match.Group ?? string.Empty;
                }
            }
        }

        return
        [
            .. entries
                .GroupBy(entry => groupByEntry.GetValueOrDefault(entry.EntryId, string.Empty))
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => (group.Key, (IReadOnlyList<SeededEntry>)[.. group])),
        ];
    }

    // --- Gruppeneinteilung -------------------------------------------------

    /// <summary>
    /// Verteilt die Meldungen nach dem Schlangenprinzip auf die Gruppen: 1, 2, 3,
    /// 4 hin, 5, 6, 7, 8 zurück.
    ///
    /// Reihum zu verteilen wäre einfacher und schlechter — bei vier Gruppen
    /// bekäme Gruppe A die Setzungen 1 und 5, Gruppe D die 4 und 8, und die
    /// Gruppen wären systematisch ungleich stark.
    /// </summary>
    internal static IReadOnlyList<(string Name, IReadOnlyList<SeededEntry> Members)> SplitIntoGroups(
        IReadOnlyList<SeededEntry> entries,
        int groupCount)
    {
        if (groupCount < 1)
        {
            throw new DomainException($"Eine Phase braucht mindestens eine Gruppe, hatte {groupCount}.");
        }

        var ordered = entries
            .OrderBy(e => e.Seed ?? int.MaxValue)
            .ThenBy(e => e.DisplayName, StringComparer.Ordinal)
            .ToList();

        var buckets = Enumerable.Range(0, groupCount).Select(_ => new List<SeededEntry>()).ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            var row = index / groupCount;
            var column = index % groupCount;

            buckets[row % 2 == 0 ? column : groupCount - 1 - column].Add(ordered[index]);
        }

        return [.. buckets.Select((members, index) => (GroupName(index, groupCount), (IReadOnlyList<SeededEntry>)members))];
    }

    /// <summary>
    /// „Gruppe A", „Gruppe B" … — bei nur einer Gruppe gibt es keinen Namen. Eine
    /// Liga in „Gruppe A" auszuweisen wäre eine Gruppe, die es nicht gibt.
    ///
    /// Ab der 27. Gruppe wären die Buchstaben aufgebraucht; so weit lässt es
    /// <see cref="PhaseDefinition.Validate"/> nicht kommen.
    /// </summary>
    internal static string GroupName(int index, int groupCount) =>
        groupCount == 1 ? string.Empty : $"Gruppe {(char)('A' + index)}";

    // --- Paarungen ---------------------------------------------------------

    /// <summary>
    /// Kreismethode: einer bleibt stehen, die übrigen rotieren. Bei ungerader
    /// Teilnehmerzahl setzt in jeder Runde genau einer aus — dafür steht ein
    /// gedachter Platzhalter im Kreis, dessen Paarungen entfallen.
    /// </summary>
    private static void AddGroupPairings(
        IReadOnlyList<SeededEntry> members,
        string groupName,
        PhaseDefinition definition,
        List<Pairing> pairings)
    {
        if (members.Count < 2)
        {
            return;
        }

        var circle = members.Select(m => (SeededEntry?)m).ToList();
        if (circle.Count % 2 == 1)
        {
            circle.Add(null);
        }

        var half = circle.Count / 2;
        var roundsPerLeg = circle.Count - 1;
        var round = 0;

        for (var leg = 0; leg < definition.Encounters; leg++)
        {
            for (var step = 0; step < roundsPerLeg; step++)
            {
                round++;
                var position = 0;

                for (var index = 0; index < half; index++)
                {
                    var home = circle[index];
                    var away = circle[circle.Count - 1 - index];

                    if (home is null || away is null)
                    {
                        continue;
                    }

                    // In der Rückrunde tauschen die Seiten. Beim Tennis ist das
                    // ohne Belang, in einer Liga mit Heimrecht nicht — und die
                    // Paarungserzeugung soll nicht davon abhängen, was das
                    // Anzeigeformat daraus macht.
                    var (side1, side2) = leg % 2 == 0 ? (home, away) : (away, home);

                    pairings.Add(new Pairing(
                        Round: round,
                        Position: ++position,
                        Side1: side1.Origin,
                        Side2: side2.Origin,
                        Label: $"Runde {round}",
                        Group: string.IsNullOrEmpty(groupName) ? null : groupName));
                }

                Rotate(circle);
            }
        }
    }

    /// <summary>Der erste bleibt stehen, alle anderen rücken eine Position weiter.</summary>
    private static void Rotate(List<SeededEntry?> circle)
    {
        var last = circle[^1];
        circle.RemoveAt(circle.Count - 1);
        circle.Insert(1, last);
    }

    // --- Tabelle -----------------------------------------------------------

    private static TableRecord Accumulate(
        SeededEntry entry,
        string groupName,
        PhaseState state,
        bool withGroup)
    {
        int played = 0, won = 0, lost = 0, points = 0;
        int setsWon = 0, setsLost = 0, gamesWon = 0, gamesLost = 0;

        foreach (var match in state.Matches)
        {
            // Ein Freilos wurde nicht gespielt und zählt in keiner Statistik.
            if (match.Score is not { } score || score.Outcome == MatchOutcome.Bye)
            {
                continue;
            }

            var side = SideOf(match, entry.EntryId);
            if (side is not { } mine)
            {
                continue;
            }

            var other = mine == 1 ? 2 : 1;

            played++;
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
            withGroup ? groupName : null,
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

    private static int? SideOf(Match match, Guid entryId) =>
        match.Side1.EntryId == entryId ? 1
        : match.Side2.EntryId == entryId ? 2
        : null;

    /// <summary>
    /// Sortiert die Tabelle: erst Punkte, dann die Tiebreaker-Kette innerhalb
    /// jeder punktgleichen Gruppe.
    /// </summary>
    private static IEnumerable<Standing> Rank(
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

    internal static TiebreakContext BuildContext(PhaseState state)
    {
        var headToHead = new Dictionary<(Guid Winner, Guid Loser), int>();
        var wins = new Dictionary<Guid, int>();
        var opponents = new Dictionary<Guid, List<Guid>>();

        foreach (var match in state.Matches)
        {
            if (match.Score is not { } score
                || score.Outcome == MatchOutcome.Bye
                || match.WinnerEntryId is not { } winner
                || match.LoserEntryId is not { } loser)
            {
                continue;
            }

            headToHead[(winner, loser)] = headToHead.GetValueOrDefault((winner, loser)) + 1;
            wins[winner] = wins.GetValueOrDefault(winner) + 1;

            Opponents(opponents, winner).Add(loser);
            Opponents(opponents, loser).Add(winner);
        }

        // Buchholz: die Summe der Siege aller Gegner. Wer gegen die Starken
        // spielen musste, steht bei Punktgleichheit vorn.
        var buchholz = opponents.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Sum(opponent => wins.GetValueOrDefault(opponent)));

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
