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

        var tables = groups
            .Select(group => group.Members
                .Select(entry => StandingsBuilder.Accumulate(
                    entry, groups.Count > 1 ? group.Name : null, state))
                .ToList())
            .ToList();

        // Der Tiebreak-Kontext spannt die ganze Phase, gerankt wird je Gruppe:
        // Buchholz eines Gruppenspielers hängt an den Punkten seiner Gegner, und
        // die stehen in derselben Gruppe, aber gerechnet wird über die eine
        // Tabelle, die es schon gibt.
        var context = StandingsBuilder.ContextOf([.. tables.SelectMany(table => table)], state.Matches);

        return new Standings(
        [
            .. tables.SelectMany(table =>
                StandingsBuilder.Rank(table, state.Definition.Tiebreakers, context)),
        ]);
    }

    /// <summary>
    /// Alle Paarungen entstehen beim Auslosen; eine Korrektur macht keine davon
    /// hinfällig. Wer im Verlauf paart, muss hier antworten (ADR-0001).
    /// </summary>
    public IReadOnlyList<Guid> ObsoletePairings(PhaseState state) => [];

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
    ///
    /// Dass jede Gruppe mindestens zwei Mitglieder hat, ist vorher geprüft:
    /// <see cref="RequirePlayableGroups"/> weist eine Auslosung ab, in der eine
    /// Gruppe allein bliebe.
    /// </summary>
    private static void AddGroupPairings(
        IReadOnlyList<SeededEntry> members,
        string groupName,
        PhaseDefinition definition,
        List<Pairing> pairings)
    {
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

}
