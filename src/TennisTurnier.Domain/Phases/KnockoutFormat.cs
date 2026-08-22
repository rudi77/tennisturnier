using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Phases;

/// <summary>
/// K.-o.-System mit Setzliste, Freilosen und optionalem Spiel um Platz 3.
///
/// Der vollständige Baum entsteht in einem Zug: die erste Runde bekommt die
/// Meldungen, alle weiteren Runden bekommen „Sieger aus …"-Referenzen. Damit
/// ist der Draw fertig, bevor ein Ball gespielt wurde — Grundlage der
/// öffentlichen Vorschau aus ADR-0003.
/// </summary>
public sealed class KnockoutFormat : IPhaseFormat
{
    public const string ThirdPlaceLabel = "Spiel um Platz 3";

    public IReadOnlyList<Pairing> GeneratePairings(PhaseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Der Baum wird einmal gebaut. Danach werden nur noch Referenzen
        // aufgelöst, was Sache der Phase ist, nicht des Formats.
        return state.Matches.Count > 0 ? [] : BuildBracket(state);
    }

    public bool IsComplete(PhaseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.Matches.Count > 0 && state.Matches.All(m => m.Status == MatchStatus.Finished);
    }

    /// <summary>
    /// Die Endplatzierung: Sieger, Finalist, danach das Spiel um Platz 3, danach
    /// die Ausgeschiedenen nach der Runde ihres Ausscheidens.
    ///
    /// Eine echte Tabelle gibt es im K.-o.-System nicht — wer in derselben Runde
    /// ausscheidet, ist gleichrangig. Genau das wird hier auch so ausgewiesen,
    /// statt eine Rangfolge zu erfinden, die es nicht gibt.
    /// </summary>
    public Standings ComputeStandings(PhaseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Nur besetzte Plätze — vor dem Ende einer Vorphase sind die Startplätze
        // dieser Phase bloß Gruppenplätze, hinter denen noch niemand steht.
        var settled = state.Entries.Where(e => e.IsSettled).ToList();
        var byEntry = settled.ToDictionary(e => e.EntryId);
        var records = settled.ToDictionary(e => e.EntryId, e => new Record(e));

        // Ein Freilos wird übersprungen: es wurde nicht gespielt und zählt
        // deshalb weder als Match noch als Sieg. Es halb zu zählen — als Sieg,
        // aber nicht als Begegnung — hinterließe eine Tabelle, in der Siege und
        // Niederlagen nicht zur Zahl der Spiele passen.
        foreach (var match in state.Matches.Where(m => m.Score is { Outcome: not MatchOutcome.Bye }))
        {
            Account(match, records);
        }

        var final = FinalMatch(state);
        var thirdPlace = state.Matches.FirstOrDefault(m => m.Label == ThirdPlaceLabel);
        var maxRound = state.Matches.Count == 0 ? 0 : state.Matches.Max(m => m.Round);

        var groups = records.Values
            .GroupBy(r => PlacementRank(r, final, thirdPlace, maxRound))
            .OrderBy(group => group.Key)
            .ToList();

        var places = new List<Standing>(records.Count);
        var rank = 1;

        foreach (var group in groups)
        {
            // Alle einer Platzierungsgruppe bekommen denselben Rang, und der
            // nächste Rang setzt hinter der ganzen Gruppe an — geteilter dritter
            // Platz, danach der fünfte. Die Setzung ordnet innerhalb der Gruppe
            // nur die Ausgabe, sie entscheidet nichts: im K.-o.-System hat
            // niemand gegen den anderen gespielt, und ein vierter Platz, den es
            // nicht gibt, wäre eine Erfindung mit Pokalfolgen.
            var members = group
                .OrderBy(r => byEntry[r.Entry.EntryId].Seed ?? int.MaxValue)
                .ThenBy(r => r.Entry.DisplayName, StringComparer.Ordinal)
                .ToList();

            places.AddRange(members.Select(record => record.ToStanding(rank)));
            rank += members.Count;
        }

        return new Standings(places);
    }

    /// <summary>
    /// Der Baum entsteht in einem Zug; eine Korrektur macht keine Paarung
    /// hinfällig, sondern löst nur die Referenzen dahinter neu auf (ADR-0001).
    /// </summary>
    public IReadOnlyList<Guid> ObsoletePairings(PhaseState state) => [];

    // --- Aufbau des Baums --------------------------------------------------

    private static IReadOnlyList<Pairing> BuildBracket(PhaseState state)
    {
        var seeded = OrderForBracket(state.Entries);
        if (seeded.Count < 2)
        {
            throw new DomainException(
                $"Ein K.-o.-Baum braucht mindestens zwei Teilnehmer, hatte {seeded.Count}.");
        }

        var bracketSize = NextPowerOfTwo(seeded.Count);
        var rounds = (int)Math.Log2(bracketSize);

        var pairings = new List<Pairing>();
        BuildFirstRound(seeded, bracketSize, rounds, pairings);
        BuildLaterRounds(rounds, bracketSize, pairings);

        if (state.Definition.ThirdPlaceMatch && rounds >= 2)
        {
            AddThirdPlaceMatch(pairings, rounds);
        }

        return pairings;
    }

    /// <summary>
    /// Erste Runde: die Bracket-Positionen werden mit Meldungen oder Freilosen
    /// besetzt. Die Freilose gehen an die Positionen der höchsten Setzungen —
    /// das ist der Zweck einer Setzliste.
    /// </summary>
    private static void BuildFirstRound(
        IReadOnlyList<SeededEntry> seeded,
        int bracketSize,
        int rounds,
        List<Pairing> pairings)
    {
        var order = SeedOrder(bracketSize);
        var slots = new ParticipantRef[bracketSize];

        for (var slot = 0; slot < bracketSize; slot++)
        {
            var seedNumber = order[slot];
            slots[slot] = seedNumber <= seeded.Count
                ? seeded[seedNumber - 1].Origin
                : ParticipantRef.ByeSlot;
        }

        for (var position = 0; position < bracketSize / 2; position++)
        {
            pairings.Add(new Pairing(
                Round: 1,
                Position: position + 1,
                Side1: slots[position * 2],
                Side2: slots[(position * 2) + 1],
                Label: RoundLabel(1, rounds)));
        }
    }

    /// <summary>
    /// Alle weiteren Runden bestehen aus „Sieger aus …"-Referenzen. Weil die
    /// Match-Ids erst beim Anlegen entstehen, trägt die Paarung hier zunächst
    /// offene Referenzen; die Phase ersetzt sie durch die echten Vorgänger.
    /// </summary>
    private static void BuildLaterRounds(int rounds, int bracketSize, List<Pairing> pairings)
    {
        var matchesInRound = bracketSize / 2;

        for (var round = 2; round <= rounds; round++)
        {
            matchesInRound /= 2;

            for (var position = 1; position <= matchesInRound; position++)
            {
                pairings.Add(new Pairing(
                    Round: round,
                    Position: position,
                    Side1: ParticipantRef.Open,
                    Side2: ParticipantRef.Open,
                    Label: RoundLabel(round, rounds)));
            }
        }
    }

    private static void AddThirdPlaceMatch(List<Pairing> pairings, int rounds) =>
        pairings.Add(new Pairing(
            Round: rounds,
            Position: 2,
            Side1: ParticipantRef.Open,
            Side2: ParticipantRef.Open,
            Label: ThirdPlaceLabel));

    /// <summary>
    /// Setzliste zuerst, danach die Ungesetzten in der Reihenfolge ihrer
    /// Meldung. Die Ungesetzten werden bewusst nicht gemischt: der Draw soll
    /// nachvollziehbar sein, und wer losen will, vergibt vorher Setzpositionen.
    /// </summary>
    private static IReadOnlyList<SeededEntry> OrderForBracket(IReadOnlyList<SeededEntry> entries) =>
        entries
            .OrderBy(e => e.Seed ?? int.MaxValue)
            .ThenBy(e => e.DisplayName, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Die Standard-Setzlistenverteilung, als Setznummer je Bracket-Position:
    /// für acht Plätze <c>1, 8, 4, 5, 2, 7, 3, 6</c> — also 1 gegen 8, 4 gegen 5,
    /// 2 gegen 7, 3 gegen 6.
    ///
    /// Sie sorgt dafür, dass sich die beiden Topgesetzten frühestens im Finale
    /// begegnen und die vier Topgesetzten in vier verschiedenen Vierteln stehen.
    /// Zugleich fallen die Freilose an die höchsten Setzungen, weil die
    /// überzähligen Setznummern gerade deren Gegner sind — genau das ist der
    /// Zweck einer Setzliste.
    ///
    /// Das Verfahren ist rekursiv: das Bracket der Größe 2n entsteht aus dem der
    /// Größe n, indem jede Setznummer s durch das Paar (s, 2n+1-s) ersetzt wird.
    /// </summary>
    internal static IReadOnlyList<int> SeedOrder(int bracketSize)
    {
        var order = new List<int> { 1 };

        while (order.Count < bracketSize)
        {
            var size = order.Count * 2;
            var next = new List<int>(size);

            foreach (var seed in order)
            {
                next.Add(seed);
                next.Add(size + 1 - seed);
            }

            order = next;
        }

        return order;
    }

    internal static int NextPowerOfTwo(int count)
    {
        var size = 1;
        while (size < count)
        {
            size *= 2;
        }

        return size;
    }

    internal static string RoundLabel(int round, int rounds) => (rounds - round) switch
    {
        0 => "Finale",
        1 => "Halbfinale",
        2 => "Viertelfinale",
        3 => "Achtelfinale",
        _ => $"Runde {round}",
    };

    // --- Endplatzierung ----------------------------------------------------

    private static Match? FinalMatch(PhaseState state)
    {
        if (state.Matches.Count == 0)
        {
            return null;
        }

        var lastRound = state.Matches.Max(m => m.Round);

        return state.Matches.FirstOrDefault(m => m.Round == lastRound && m.Label != ThirdPlaceLabel);
    }

    private static void Account(Match match, Dictionary<Guid, Record> records)
    {
        var score = match.Score!;

        if (match.WinnerEntryId is { } winnerId && records.TryGetValue(winnerId, out var winner))
        {
            winner.AddWin(score, score.WinnerSide);
        }

        if (match.LoserEntryId is { } loserId && records.TryGetValue(loserId, out var loser))
        {
            loser.AddLoss(score, score.LoserSide, match.Round);
        }
    }

    /// <summary>
    /// Sieger 1, Finalist 2, dann Platz 3 und 4 aus dem Spiel um Platz 3, danach
    /// nach Ausscheiderunde. Ohne Spiel um Platz 3 sind beide Halbfinalverlierer
    /// gleichrangig — und werden auch so ausgewiesen.
    /// </summary>
    private static int PlacementRank(Record record, Match? final, Match? thirdPlace, int maxRound)
    {
        if (final?.WinnerEntryId == record.Entry.EntryId)
        {
            return 1;
        }

        if (final?.LoserEntryId == record.Entry.EntryId)
        {
            return 2;
        }

        if (thirdPlace?.WinnerEntryId == record.Entry.EntryId)
        {
            return 3;
        }

        if (thirdPlace?.LoserEntryId == record.Entry.EntryId)
        {
            return 4;
        }

        // Wer später ausscheidet, steht weiter vorn. Noch im Turnier stehende
        // Teilnehmer kommen vor allen Ausgeschiedenen.
        return record.EliminatedInRound is { } round ? 100 + (maxRound - round) : 5;
    }

    private sealed class Record
    {
        public Record(SeededEntry entry) => Entry = entry;

        public SeededEntry Entry { get; }

        public int Played { get; private set; }

        public int Won { get; private set; }

        public int Lost { get; private set; }

        public int SetsWon { get; private set; }

        public int SetsLost { get; private set; }

        public int GamesWon { get; private set; }

        public int GamesLost { get; private set; }

        public int? EliminatedInRound { get; private set; }

        public void AddWin(Score score, int side)
        {
            Accumulate(score, side);
            Won++;
        }

        public void AddLoss(Score score, int side, int round)
        {
            Accumulate(score, side);
            Lost++;
            EliminatedInRound = round;
        }

        private void Accumulate(Score score, int side)
        {
            var other = side == 1 ? 2 : 1;

            Played++;
            SetsWon += score.SetsWonBy(side);
            SetsLost += score.SetsWonBy(other);
            GamesWon += score.GamesWonBy(side);
            GamesLost += score.GamesWonBy(other);
        }

        public Standing ToStanding(int rank) => new(
            rank,
            Entry.EntryId,
            Entry.DisplayName,
            Group: null,
            Played,
            Won,
            Lost,
            Points: Won,
            SetsWon,
            SetsLost,
            GamesWon,
            GamesLost);
    }
}
