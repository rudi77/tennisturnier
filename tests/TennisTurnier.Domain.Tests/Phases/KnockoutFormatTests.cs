using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;

namespace TennisTurnier.Domain.Tests.Phases;

public sealed class KnockoutFormatTests
{
    private static readonly MatchFormat Standard = new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6);

    private readonly KnockoutFormat _format = new();
    private readonly Guid _tournamentId = Guid.NewGuid();

    private static PhaseDefinition Definition(bool thirdPlaceMatch = false) => new()
    {
        Ordinal = 1,
        Format = PhaseFormatKind.Knockout,
        ThirdPlaceMatch = thirdPlaceMatch,
    };

    /// <summary>Teilnehmer mit Setzung 1..seededCount, der Rest ungesetzt.</summary>
    private static IReadOnlyList<SeededEntry> Entries(int count, int seededCount = 0) =>
        Enumerable.Range(1, count)
            .Select(i => new SeededEntry(Guid.NewGuid(), i <= seededCount ? i : null, $"Spielerin {i:00}"))
            .ToList();

    private Phase NewPhase() => new(Guid.NewGuid(), _tournamentId, 1, PhaseFormatKind.Knockout, "Hauptfeld");

    private Phase BuildPhase(IReadOnlyList<SeededEntry> entries, bool thirdPlaceMatch = false)
    {
        var phase = NewPhase();
        var state = new PhaseState(phase.Id, Definition(thirdPlaceMatch), Standard, entries, phase.Matches);

        phase.AddPairings(_format.GeneratePairings(state));
        return phase;
    }

    private PhaseState StateOf(Phase phase, IReadOnlyList<SeededEntry> entries, bool thirdPlaceMatch = false) =>
        new(phase.Id, Definition(thirdPlaceMatch), Standard, entries, phase.Matches);

    // --- Bracket-Aufbau ----------------------------------------------------

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    [InlineData(8, 7)]
    [InlineData(16, 15)]
    public void Ein_volles_Feld_ergibt_genau_n_minus_eins_Matches(int participants, int expectedMatches)
    {
        var phase = BuildPhase(Entries(participants));

        Assert.Equal(expectedMatches, phase.Matches.Count);
    }

    [Theory]
    [InlineData(3, 4)]
    [InlineData(5, 8)]
    [InlineData(12, 16)]
    [InlineData(17, 32)]
    public void Ein_unvolles_Feld_wird_auf_die_naechste_Zweierpotenz_aufgefuellt(int participants, int bracketSize)
    {
        var phase = BuildPhase(Entries(participants));

        Assert.Equal(bracketSize / 2, phase.Matches.Count(m => m.Round == 1));
        Assert.Equal(bracketSize - participants, CountByes(phase));
    }

    private static int CountByes(Phase phase) =>
        phase.Matches.Count(m => m.Side1.IsBye) + phase.Matches.Count(m => m.Side2.IsBye);

    [Fact]
    public void Die_Freilose_gehen_an_die_hoechsten_Setzungen()
    {
        // Das ist der Zweck einer Setzliste: der Topgesetzte soll in der ersten
        // Runde nicht gegen den Zweitgesetzten spielen müssen.
        var entries = Entries(5, seededCount: 5);
        var phase = BuildPhase(entries);

        var topSeed = entries[0].EntryId;
        var matchOfTopSeed = phase.Matches.Single(m =>
            m.Round == 1 && (m.Side1.EntryId == topSeed || m.Side2.EntryId == topSeed));

        Assert.True(matchOfTopSeed.HasBye);
    }

    [Fact]
    public void Die_beiden_Topgesetzten_treffen_fruehestens_im_Finale_aufeinander()
    {
        var entries = Entries(8, seededCount: 8);
        var phase = BuildPhase(entries);

        var first = entries[0].EntryId;
        var second = entries[1].EntryId;

        // Sie stehen in verschiedenen Hälften des Baums: das erste Match des
        // Topgesetzten liegt in der oberen, das des Zweitgesetzten in der unteren.
        var firstPosition = PositionOf(phase, first);
        var secondPosition = PositionOf(phase, second);
        var half = phase.Matches.Count(m => m.Round == 1) / 2;

        Assert.True(firstPosition <= half);
        Assert.True(secondPosition > half);
    }

    [Fact]
    public void Die_vier_Topgesetzten_liegen_in_vier_verschiedenen_Vierteln()
    {
        var entries = Entries(8, seededCount: 8);
        var phase = BuildPhase(entries);
        var matchesInRoundOne = phase.Matches.Count(m => m.Round == 1);
        var quarterSize = matchesInRoundOne / 4;

        var quarters = entries
            .Take(4)
            .Select(e => (PositionOf(phase, e.EntryId) - 1) / quarterSize)
            .ToList();

        Assert.Equal(4, quarters.Distinct().Count());
    }

    private static int PositionOf(Phase phase, Guid entryId) =>
        phase.Matches.Single(m =>
            m.Round == 1 && (m.Side1.EntryId == entryId || m.Side2.EntryId == entryId)).Position;

    [Theory]
    [InlineData(2, new[] { 1, 2 })]
    [InlineData(4, new[] { 1, 4, 2, 3 })]
    [InlineData(8, new[] { 1, 8, 4, 5, 2, 7, 3, 6 })]
    [InlineData(16, new[] { 1, 16, 8, 9, 4, 13, 5, 12, 2, 15, 7, 10, 3, 14, 6, 11 })]
    public void Die_Setzlistenverteilung_folgt_dem_Standard(int bracketSize, int[] expected)
    {
        // Ausgeschrieben statt berechnet: diese Zahlenfolge ist die eigentliche
        // Zusicherung. Sie neben der Implementierung noch einmal herzuleiten
        // hieße, denselben Fehler zweimal zu machen.
        Assert.Equal(expected, KnockoutFormat.SeedOrder(bracketSize));
    }

    [Fact]
    public void Die_Runden_tragen_sprechende_Namen()
    {
        var phase = BuildPhase(Entries(8));

        Assert.Equal("Viertelfinale", phase.Matches.First(m => m.Round == 1).Label);
        Assert.Equal("Halbfinale", phase.Matches.First(m => m.Round == 2).Label);
        Assert.Equal("Finale", phase.Matches.First(m => m.Round == 3).Label);
    }

    [Fact]
    public void Die_spaeteren_Runden_verweisen_auf_die_Sieger_der_vorigen()
    {
        var phase = BuildPhase(Entries(4));

        var firstRound = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();
        var final = phase.Matches.Single(m => m.Round == 2);

        Assert.Equal(ParticipantRef.FromWinnerOf(firstRound[0].Id), final.Side1.Origin);
        Assert.Equal(ParticipantRef.FromWinnerOf(firstRound[1].Id), final.Side2.Origin);
    }

    [Fact]
    public void Der_Draw_steht_bevor_ein_Ball_gespielt_ist()
    {
        // Grundlage der öffentlichen Vorschau aus ADR-0003: der ganze Baum
        // existiert, obwohl noch niemand über die erste Runde hinaus feststeht.
        var phase = BuildPhase(Entries(8));

        Assert.Equal(7, phase.Matches.Count);
        Assert.All(phase.Matches.Where(m => m.Round == 1), m => Assert.Equal(MatchStatus.Ready, m.Status));
        Assert.All(phase.Matches.Where(m => m.Round > 1), m => Assert.Equal(MatchStatus.Pending, m.Status));
    }

    [Fact]
    public void Ein_Spiel_um_Platz_3_bekommt_die_Halbfinalverlierer()
    {
        var phase = BuildPhase(Entries(4), thirdPlaceMatch: true);

        var semiFinals = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();
        var thirdPlace = phase.Matches.Single(m => m.Label == KnockoutFormat.ThirdPlaceLabel);

        Assert.Equal(ParticipantRef.FromLoserOf(semiFinals[0].Id), thirdPlace.Side1.Origin);
        Assert.Equal(ParticipantRef.FromLoserOf(semiFinals[1].Id), thirdPlace.Side2.Origin);
    }

    [Fact]
    public void Ein_Feld_unter_zwei_Teilnehmern_ergibt_kein_Turnier()
    {
        var phase = NewPhase();
        var state = StateOf(phase, Entries(1));

        Assert.Throws<DomainException>(() => _format.GeneratePairings(state));
    }

    [Fact]
    public void Ein_bereits_gebautes_Bracket_wird_nicht_erneut_erzeugt()
    {
        var entries = Entries(4);
        var phase = BuildPhase(entries);

        Assert.Empty(_format.GeneratePairings(StateOf(phase, entries)));
    }

    // --- Freilose und Propagation -----------------------------------------

    [Fact]
    public void Ein_Freilos_entscheidet_sich_beim_Aufbau_von_selbst()
    {
        var phase = BuildPhase(Entries(3));

        var byeMatch = phase.Matches.Single(m => m.Round == 1 && m.HasBye);

        Assert.Equal(MatchStatus.Finished, byeMatch.Status);
        Assert.Equal(MatchOutcome.Bye, byeMatch.Score!.Outcome);
        Assert.NotNull(byeMatch.WinnerEntryId);
    }

    [Fact]
    public void Der_Freilos_Sieger_steht_sofort_in_der_naechsten_Runde()
    {
        var entries = Entries(3, seededCount: 3);
        var phase = BuildPhase(entries);

        var final = phase.Matches.Single(m => m.Round == 2);
        var advanced = phase.Matches.Single(m => m.HasBye).WinnerEntryId;

        Assert.Contains(advanced, new[] { final.Side1.EntryId, final.Side2.EntryId });
    }

    [Fact]
    public void Ein_Ergebnis_wird_an_die_naechste_Runde_weitergereicht()
    {
        var entries = Entries(4);
        var phase = BuildPhase(entries);
        var firstRound = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();
        var final = phase.Matches.Single(m => m.Round == 2);

        var winner = firstRound[0].Side1.EntryId;
        phase.RecordResult(firstRound[0].Id, Score.Played([new SetScore(6, 4), new SetScore(6, 2)], Standard));

        Assert.Equal(winner, final.Side1.EntryId);
        Assert.Null(final.Side2.EntryId);
        Assert.Equal(MatchStatus.Pending, final.Status);
    }

    [Fact]
    public void Erst_beide_Ergebnisse_machen_das_Finale_spielbar()
    {
        var phase = BuildPhase(Entries(4));
        var firstRound = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();
        var final = phase.Matches.Single(m => m.Round == 2);

        foreach (var match in firstRound)
        {
            phase.RecordResult(match.Id, Score.Played([new SetScore(6, 4), new SetScore(6, 2)], Standard));
        }

        Assert.Equal(MatchStatus.Ready, final.Status);
    }

    [Fact]
    public void Ein_Nichtantreten_bringt_den_Gegner_weiter()
    {
        var phase = BuildPhase(Entries(4));
        var match = phase.Matches.First(m => m.Round == 1);
        var expected = match.Side2.EntryId;

        phase.RecordResult(match.Id, Score.Walkover(absentSide: 1));

        Assert.Equal(expected, match.WinnerEntryId);
        Assert.Equal(expected, phase.Matches.Single(m => m.Round == 2).Side1.EntryId);
    }

    [Fact]
    public void Ein_Match_mit_Freilos_bekommt_kein_regulaeres_Ergebnis()
    {
        var phase = BuildPhase(Entries(3));
        var byeMatch = phase.Matches.Single(m => m.Round == 1 && m.HasBye);

        // Es ist bereits als Freilos entschieden; ein Spielergebnis wäre Unsinn.
        Assert.Throws<DomainException>(
            () => byeMatch.RecordResult(Score.Played([new SetScore(6, 0), new SetScore(6, 0)], Standard)));
    }

    // --- Korrektur ---------------------------------------------------------

    [Fact]
    public void Ein_Ergebnis_laesst_sich_zuruecknehmen_solange_die_Folge_offen_ist()
    {
        var phase = BuildPhase(Entries(4));
        var match = phase.Matches.First(m => m.Round == 1);
        var final = phase.Matches.Single(m => m.Round == 2);

        phase.RecordResult(match.Id, Score.Played([new SetScore(6, 4), new SetScore(6, 2)], Standard));
        Assert.NotNull(final.Side1.EntryId);

        phase.ClearResult(match.Id);

        Assert.Null(match.Score);
        Assert.Null(final.Side1.EntryId);
        Assert.Equal(ParticipantRef.FromWinnerOf(match.Id), final.Side1.Origin);
    }

    [Fact]
    public void Ein_Ergebnis_laesst_sich_nicht_zuruecknehmen_wenn_das_Folgematch_gespielt_ist()
    {
        // Sonst stünde in der nächsten Runde jemand, der laut korrigiertem
        // Ergebnis nie hätte antreten dürfen.
        var phase = BuildPhase(Entries(4));
        var firstRound = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();
        var final = phase.Matches.Single(m => m.Round == 2);

        foreach (var match in firstRound)
        {
            phase.RecordResult(match.Id, Score.Played([new SetScore(6, 4), new SetScore(6, 2)], Standard));
        }

        phase.RecordResult(final.Id, Score.Played([new SetScore(6, 3), new SetScore(6, 3)], Standard));

        var ex = Assert.Throws<DomainException>(() => phase.ClearResult(firstRound[0].Id));
        Assert.Contains("Folgematch", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nach_Ruecknahme_des_Folgematches_ist_die_Korrektur_moeglich()
    {
        var phase = BuildPhase(Entries(4));
        var firstRound = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();
        var final = phase.Matches.Single(m => m.Round == 2);

        foreach (var match in firstRound)
        {
            phase.RecordResult(match.Id, Score.Played([new SetScore(6, 4), new SetScore(6, 2)], Standard));
        }

        phase.RecordResult(final.Id, Score.Played([new SetScore(6, 3), new SetScore(6, 3)], Standard));

        phase.ClearResult(final.Id);
        phase.ClearResult(firstRound[0].Id);

        Assert.Null(firstRound[0].Score);
    }

    [Fact]
    public void Ein_korrigiertes_Ergebnis_schickt_den_neuen_Sieger_weiter()
    {
        var phase = BuildPhase(Entries(4));
        var match = phase.Matches.First(m => m.Round == 1);
        var final = phase.Matches.Single(m => m.Round == 2);

        phase.RecordResult(match.Id, Score.Played([new SetScore(6, 4), new SetScore(6, 2)], Standard));
        var firstWinner = final.Side1.EntryId;

        phase.ClearResult(match.Id);
        phase.RecordResult(match.Id, Score.Played([new SetScore(4, 6), new SetScore(2, 6)], Standard));

        Assert.NotEqual(firstWinner, final.Side1.EntryId);
        Assert.Equal(match.Side2.EntryId, final.Side1.EntryId);
    }

    // --- Abschluss und Platzierung ----------------------------------------

    [Fact]
    public void Eine_Phase_ist_erst_mit_dem_letzten_Ergebnis_abgeschlossen()
    {
        var entries = Entries(4);
        var phase = BuildPhase(entries);

        Assert.False(_format.IsComplete(StateOf(phase, entries)));

        PlayOut(phase, entries);

        Assert.True(_format.IsComplete(StateOf(phase, entries)));
        Assert.Equal(PhaseStatus.Completed, phase.Status);
    }

    [Fact]
    public void Die_Platzierung_nennt_Sieger_und_Finalist_zuerst()
    {
        var entries = Entries(4);
        var phase = BuildPhase(entries);
        PlayOut(phase, entries);

        var final = phase.Matches.Single(m => m.Round == 2);
        var standings = _format.ComputeStandings(StateOf(phase, entries));

        Assert.Equal(final.WinnerEntryId, standings.Places[0].EntryId);
        Assert.Equal(final.LoserEntryId, standings.Places[1].EntryId);
        Assert.Equal(1, standings.Places[0].Rank);
    }

    [Fact]
    public void Mit_Spiel_um_Platz_3_stehen_auch_die_Plaetze_3_und_4_fest()
    {
        var entries = Entries(4);
        var phase = BuildPhase(entries, thirdPlaceMatch: true);
        PlayOut(phase, entries);

        var thirdPlace = phase.Matches.Single(m => m.Label == KnockoutFormat.ThirdPlaceLabel);
        var standings = _format.ComputeStandings(StateOf(phase, entries, thirdPlaceMatch: true));

        Assert.Equal(thirdPlace.WinnerEntryId, standings.Places[2].EntryId);
        Assert.Equal(thirdPlace.LoserEntryId, standings.Places[3].EntryId);
    }

    [Fact]
    public void Ein_Freilos_zaehlt_in_keiner_Statistik()
    {
        // Es wurde nicht gespielt — weder Sätze noch Spiele noch ein Match.
        var entries = Entries(3, seededCount: 3);
        var phase = BuildPhase(entries);
        PlayOut(phase, entries);

        var standings = _format.ComputeStandings(StateOf(phase, entries));
        var champion = standings.Places[0];

        Assert.True(champion.Played <= 2);
        Assert.Equal(champion.Won + champion.Lost, champion.Played);
    }

    /// <summary>Spielt alle noch offenen Matches aus, Seite 1 gewinnt jeweils.</summary>
    private static void PlayOut(Phase phase, IReadOnlyList<SeededEntry> entries)
    {
        _ = entries;

        for (var guard = 0; guard < 100; guard++)
        {
            var next = phase.Matches.FirstOrDefault(m => m.Status == MatchStatus.Ready);
            if (next is null)
            {
                return;
            }

            phase.RecordResult(next.Id, Score.Played([new SetScore(6, 4), new SetScore(6, 2)], Standard));
        }

        throw new InvalidOperationException("Die Phase ließ sich nicht zu Ende spielen.");
    }
}
