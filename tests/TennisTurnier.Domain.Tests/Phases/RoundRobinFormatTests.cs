using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;

namespace TennisTurnier.Domain.Tests.Phases;

public sealed class RoundRobinFormatTests
{
    private static readonly MatchFormat Standard = new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6);

    private static PhaseDefinition Definition(
        int groupCount = 1,
        int encounters = 1,
        ScoringRules? scoring = null,
        IReadOnlyList<Tiebreaker>? tiebreakers = null) => new()
        {
            Ordinal = 1,
            Format = PhaseFormatKind.RoundRobin,
            GroupCount = groupCount,
            Encounters = encounters,
            Scoring = scoring ?? new ScoringRules(),
            Tiebreakers = tiebreakers
                ?? [Tiebreaker.DirectEncounter, Tiebreaker.SetRatio, Tiebreaker.GameRatio, Tiebreaker.Lot],
        };

    private static IReadOnlyList<SeededEntry> Entries(int count, int seededCount = 0) =>
        [.. Enumerable.Range(1, count).Select(i =>
            new SeededEntry(Guid.NewGuid(), i <= seededCount ? i : null, $"Spielerin {i:00}"))];

    private static Phase BuildPhase(
        IReadOnlyList<SeededEntry> entries,
        PhaseDefinition? definition = null)
    {
        var phase = new Phase(Guid.NewGuid(), Guid.NewGuid(), 1, PhaseFormatKind.RoundRobin, "Gruppenphase");
        var state = new PhaseState(definition ?? Definition(), entries, phase.Matches);

        phase.AddPairings(new RoundRobinFormat().GeneratePairings(state));

        return phase;
    }

    private static PhaseState StateOf(
        Phase phase,
        IReadOnlyList<SeededEntry> entries,
        PhaseDefinition? definition = null) =>
        new(definition ?? Definition(), entries, phase.Matches);

    private static Standings StandingsOf(
        Phase phase,
        IReadOnlyList<SeededEntry> entries,
        PhaseDefinition? definition = null) =>
        new RoundRobinFormat().ComputeStandings(StateOf(phase, entries, definition));

    private static void Win(Phase phase, Match match, int side) =>
        phase.RecordResult(
            match.Id,
            Score.Played(
                side == 1
                    ? [new SetScore(6, 4), new SetScore(6, 2)]
                    : [new SetScore(4, 6), new SetScore(2, 6)],
                Standard));

    // --- Paarungen ---------------------------------------------------------

    [Theory]
    [InlineData(4, 6)]
    [InlineData(5, 10)]
    [InlineData(6, 15)]
    [InlineData(8, 28)]
    public void Jeder_spielt_genau_einmal_gegen_jeden(int count, int expected)
    {
        var phase = BuildPhase(Entries(count));

        Assert.Equal(expected, phase.Matches.Count);
    }

    [Fact]
    public void Keine_Paarung_kommt_doppelt_vor()
    {
        var phase = BuildPhase(Entries(6));

        var pairs = phase.Matches
            .Select(m => new[] { m.Side1.EntryId!.Value, m.Side2.EntryId!.Value }.Order().ToArray())
            .Select(pair => $"{pair[0]}-{pair[1]}")
            .ToList();

        Assert.Equal(pairs.Count, pairs.Distinct().Count());
    }

    [Fact]
    public void Niemand_spielt_zweimal_in_derselben_Runde()
    {
        // Das ist der Sinn der Kreismethode. Ohne sie wäre jeder Spielplan
        // unbrauchbar, weil er dieselbe Person parallel ansetzt.
        var phase = BuildPhase(Entries(8));

        foreach (var round in phase.Matches.GroupBy(m => m.Round))
        {
            var participants = round
                .SelectMany(m => new[] { m.Side1.EntryId, m.Side2.EntryId })
                .ToList();

            Assert.Equal(participants.Count, participants.Distinct().Count());
        }
    }

    [Fact]
    public void Bei_ungerader_Teilnehmerzahl_setzt_je_Runde_genau_einer_aus()
    {
        var entries = Entries(5);
        var phase = BuildPhase(entries);

        Assert.Equal(5, phase.Matches.Select(m => m.Round).Distinct().Count());

        foreach (var round in phase.Matches.GroupBy(m => m.Round))
        {
            Assert.Equal(2, round.Count());
        }
    }

    [Fact]
    public void Eine_Rueckrunde_verdoppelt_die_Begegnungen_mit_getauschten_Seiten()
    {
        var entries = Entries(4);
        var phase = BuildPhase(entries, Definition(encounters: 2));

        Assert.Equal(12, phase.Matches.Count);

        var first = phase.Matches.First();
        var reverse = phase.Matches.Single(m =>
            m.Side1.EntryId == first.Side2.EntryId && m.Side2.EntryId == first.Side1.EntryId);

        Assert.True(reverse.Round > first.Round);
    }

    // --- Gruppen -----------------------------------------------------------

    [Fact]
    public void Vier_Gruppen_zu_je_vier_ergeben_vier_Tabellen()
    {
        var entries = Entries(16, seededCount: 16);
        var phase = BuildPhase(entries, Definition(groupCount: 4));

        var groups = phase.Matches.Select(m => m.Group).Distinct().Order().ToList();

        Assert.Equal(["Gruppe A", "Gruppe B", "Gruppe C", "Gruppe D"], groups);
        Assert.Equal(24, phase.Matches.Count);
    }

    [Fact]
    public void Die_Setzungen_verteilen_sich_ueber_die_Gruppen()
    {
        // Schlangenverteilung: die vier Topgesetzten in vier verschiedene
        // Gruppen. Reihum verteilt bekäme Gruppe A die 1 und die 5, Gruppe D die
        // 4 und die 8 — die Gruppen wären systematisch ungleich stark.
        var entries = Entries(8, seededCount: 8);
        var groups = RoundRobinFormat.SplitIntoGroups(entries, 4);

        Assert.All(groups, group => Assert.Equal(2, group.Members.Count));

        foreach (var (_, members) in groups)
        {
            Assert.Single(members, m => m.Seed <= 4);
        }

        // Der Topgesetzte trifft in seiner Gruppe auf den schwächsten der zweiten
        // Vierergruppe — das ist der Zweck der Schlange.
        var first = groups.Single(g => g.Members.Any(m => m.Seed == 1));
        Assert.Contains(first.Members, m => m.Seed == 8);
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(7, 4)]
    [InlineData(3, 2)]
    public void Eine_Auslosung_mit_einer_Gruppe_ohne_Gegner_wird_abgewiesen(int participants, int groupCount)
    {
        // Regression: die Gruppen wurden strikt nach der Definition gebildet,
        // auch wenn dabei jemand allein blieb. Diese Gruppe bekam kein einziges
        // Match, ihre Teilnehmer schieden ohne ein Spiel aus, die Phase galt als
        // abgeschlossen — und die Endrunde bekam Plätze, die niemand einnehmen
        // kann. Das Turnier ließ sich nie beenden.
        var entries = Entries(participants);

        var error = Assert.Throws<DomainException>(() => BuildPhase(entries, Definition(groupCount)));

        Assert.Contains("mindestens", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Die_Gruppe_eines_Teilnehmers_kommt_aus_seinen_Matches()
    {
        // Regression: die Tabelle leitete die Gruppen zur Laufzeit erneut aus
        // Setzung und Name her, statt sie an den Matches abzulesen. Wich die
        // übergebene Menge oder ein Anzeigename ab, stand jemand in einer Gruppe,
        // in der er nie gespielt hatte — und die Endrunde wurde daraus besetzt.
        var entries = Entries(4);
        var phase = BuildPhase(entries, Definition(groupCount: 2));

        // Derselbe Stand, aber mit geändertem Anzeigenamen: an der Zugehörigkeit
        // darf sich dadurch nichts ändern.
        var renamed = entries
            .Select((entry, index) => index == 0 ? entry with { DisplayName = "Aaron" } : entry)
            .ToList();

        var table = StandingsOf(phase, renamed, Definition(groupCount: 2)).Places;

        foreach (var match in phase.Matches)
        {
            var side1 = table.Single(place => place.EntryId == match.Side1.EntryId);
            var side2 = table.Single(place => place.EntryId == match.Side2.EntryId);

            Assert.Equal(match.Group, side1.Group);
            Assert.Equal(match.Group, side2.Group);
        }
    }

    [Fact]
    public void Eine_Liga_hat_keinen_Gruppennamen()
    {
        var entries = Entries(4);
        var phase = BuildPhase(entries);

        Assert.All(phase.Matches, match => Assert.Null(match.Group));
        Assert.All(StandingsOf(phase, entries).Places, place => Assert.Null(place.Group));
    }

    // --- Tabelle -----------------------------------------------------------

    [Fact]
    public void Wer_alles_gewinnt_steht_oben()
    {
        var entries = Entries(4);
        var phase = BuildPhase(entries);
        var champion = entries[0].EntryId;

        foreach (var match in phase.Matches.ToList())
        {
            var side = match.Side1.EntryId == champion ? 1 : match.Side2.EntryId == champion ? 2 : 1;
            Win(phase, match, side);
        }

        var table = StandingsOf(phase, entries).Places;

        Assert.Equal(champion, table[0].EntryId);
        Assert.Equal(3, table[0].Won);
        Assert.Equal(6, table[0].Points);
        Assert.Equal(1, table[0].Rank);
    }

    [Fact]
    public void Das_Punktesystem_kommt_aus_der_Definition()
    {
        var entries = Entries(4);
        var definition = Definition(scoring: new ScoringRules(Win: 3, Loss: 1));
        var phase = BuildPhase(entries, definition);

        Win(phase, phase.Matches[0], side: 1);

        var table = StandingsOf(phase, entries, definition).Places;

        Assert.Equal(3, table.Single(p => p.EntryId == phase.Matches[0].Side1.EntryId).Points);
        Assert.Equal(1, table.Single(p => p.EntryId == phase.Matches[0].Side2.EntryId).Points);
    }

    [Fact]
    public void Ein_Nichtantreten_bringt_dem_Sieger_die_dafuer_vorgesehenen_Punkte()
    {
        // In manchen Ausschreibungen zählt ein kampfloser Sieg weniger als ein
        // erspielter. Das ist ein Parameter, keine Sonderbehandlung.
        var entries = Entries(4);
        var definition = Definition(scoring: new ScoringRules(Win: 2, Loss: 0, Walkover: 1));
        var phase = BuildPhase(entries, definition);
        var match = phase.Matches[0];

        phase.RecordResult(match.Id, Score.Walkover(absentSide: 2));

        var table = StandingsOf(phase, entries, definition).Places;

        Assert.Equal(1, table.Single(p => p.EntryId == match.Side1.EntryId).Points);
    }

    [Fact]
    public void Der_direkte_Vergleich_entscheidet_vor_dem_Satzverhaeltnis()
    {
        var entries = Entries(4, seededCount: 4);
        var phase = BuildPhase(entries);

        // A und B gewinnen je zwei Spiele; B hat A geschlagen, A hat das bessere
        // Satzverhältnis gegen die Übrigen. Der direkte Vergleich muss vorgehen.
        var a = entries[0].EntryId;
        var b = entries[1].EntryId;

        foreach (var match in phase.Matches.ToList())
        {
            var sides = (match.Side1.EntryId!.Value, match.Side2.EntryId!.Value);
            var winner = sides switch
            {
                _ when sides.Item1 == a && sides.Item2 == b => b,
                _ when sides.Item1 == b && sides.Item2 == a => b,
                _ when sides.Item1 == a || sides.Item2 == a => a,
                _ when sides.Item1 == b || sides.Item2 == b => b,
                _ => sides.Item1,
            };

            Win(phase, match, match.Side1.EntryId == winner ? 1 : 2);
        }

        var table = StandingsOf(phase, entries).Places;

        Assert.Equal(b, table[0].EntryId);
        Assert.Equal(a, table[1].EntryId);
    }

    [Fact]
    public void Bei_ungerader_Teilnehmerzahl_entsteht_kein_Freilos_Match()
    {
        // Anders als im K.-o.-System: wer aussetzt, hat schlicht kein Match in
        // dieser Runde. Ein Freilos-Match anzulegen brächte einen Sieg ohne
        // Gegner in die Tabelle — jeder Vergleich der Punktzahlen wäre dahin.
        var entries = Entries(5);
        var phase = BuildPhase(entries);

        Assert.All(phase.Matches, match => Assert.False(match.HasBye));
        Assert.All(entries, entry => Assert.Equal(
            4,
            phase.Matches.Count(m => m.Side1.EntryId == entry.EntryId || m.Side2.EntryId == entry.EntryId)));
    }

    [Fact]
    public void Wer_aussetzt_hat_deswegen_kein_Spiel_gespielt()
    {
        var entries = Entries(5);
        var phase = BuildPhase(entries);

        Win(phase, phase.Matches.First(m => m.Round == 1), side: 1);

        var table = StandingsOf(phase, entries).Places;

        Assert.Equal(2, table.Count(place => place.Played == 1));
        Assert.Equal(3, table.Count(place => place.Played == 0));
    }

    [Fact]
    public void Eine_Phase_ist_abgeschlossen_wenn_alle_Matches_entschieden_sind()
    {
        var entries = Entries(4);
        var phase = BuildPhase(entries);
        var format = new RoundRobinFormat();

        Assert.False(format.IsComplete(StateOf(phase, entries)));

        foreach (var match in phase.Matches.ToList())
        {
            Win(phase, match, side: 1);
        }

        Assert.True(format.IsComplete(StateOf(phase, entries)));
    }

    [Fact]
    public void Der_Baum_wird_nur_einmal_gebaut()
    {
        var entries = Entries(4);
        var phase = BuildPhase(entries);

        Assert.Empty(new RoundRobinFormat().GeneratePairings(StateOf(phase, entries)));
    }

    // --- Randfälle ---------------------------------------------------------

    [Fact]
    public void Eine_Gruppenphase_ohne_Matches_ist_nicht_fertig()
    {
        var phase = new Phase(Guid.NewGuid(), Guid.NewGuid(), 1, PhaseFormatKind.RoundRobin);

        Assert.False(new RoundRobinFormat().IsComplete(StateOf(phase, Entries(4))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Null_Gruppen_gibt_es_nicht(int groupCount)
    {
        var fehler = Assert.Throws<DomainException>(() =>
            RoundRobinFormat.SplitIntoGroups(Entries(4), groupCount));

        Assert.Contains($"hatte {groupCount}", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_einzige_Gruppe_die_allein_bliebe_wird_benannt()
    {
        // Ein Feld aus einer Person: die Gruppe hat keinen Namen, und die Absage
        // muss trotzdem sagen, wen es trifft.
        var fehler = Assert.Throws<DomainException>(() => BuildPhase(Entries(1)));

        Assert.Contains("Ohne Gegner bliebe die Gruppe", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mehrere_Gruppen_die_allein_blieben_werden_alle_benannt()
    {
        // Drei Teilnehmer auf drei Gruppen: jede bekommt einen, keine ein Match.
        var fehler = Assert.Throws<DomainException>(() =>
            BuildPhase(Entries(3), Definition(groupCount: 3)));

        Assert.Contains("Ohne Gegner blieben", fehler.Message, StringComparison.Ordinal);
        Assert.Contains("Gruppe A, Gruppe B, Gruppe C", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Gruppenphase_mit_offenen_Plaetzen_hat_noch_keine_Tabelle()
    {
        // Eine Zwischenrunde steht, bevor die Vorphase durch ist: ihre Matches
        // haben Gruppenplätze statt Meldungen. Die Tabelle darf daran nicht
        // scheitern — sie ist dann eben leer.
        var phase = new Phase(Guid.NewGuid(), Guid.NewGuid(), 2, PhaseFormatKind.RoundRobin);
        phase.AddPairings([
            new Pairing(
                1, 1,
                ParticipantRef.FromGroupPosition(Guid.NewGuid(), "Gruppe A", 1),
                ParticipantRef.FromGroupPosition(Guid.NewGuid(), "Gruppe B", 1),
                null,
                "Gruppe A"),
        ]);

        var slots = new[]
        {
            SeededEntry.ForSlot(
                ParticipantRef.FromGroupPosition(Guid.NewGuid(), "Gruppe A", 1), 1, "Erster der Gruppe A"),
        };

        var tabelle = new RoundRobinFormat().ComputeStandings(StateOf(phase, slots));

        Assert.Empty(tabelle.Places);
    }
}
