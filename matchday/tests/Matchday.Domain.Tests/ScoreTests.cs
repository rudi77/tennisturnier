using Matchday.Domain;

namespace Matchday.Domain.Tests;

public sealed class ScoreTests
{
    private static readonly MatchFormat Standard = new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6);
    private static readonly MatchFormat Advantage = new(BestOf: 3, FinalSetMode.Advantage, TiebreakAt: 6);
    private static readonly MatchFormat SingleSet = new(BestOf: 1, FinalSetMode.Regular, TiebreakAt: 6);
    private static readonly MatchFormat Kurz = new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 4);

    private static SetScore Set(int a, int b, int? tiebreak = null) => new(a, b, tiebreak);

    [Fact]
    public void Ein_glatter_Zweisatzsieg_ist_gueltig()
    {
        var score = Score.Played([Set(6, 4), Set(6, 2)], Standard);

        Assert.Equal(MatchOutcome.Normal, score.Outcome);
        Assert.Equal(1, score.WinnerSide);
        Assert.Equal(2, score.SetsWonBy(1));
        Assert.Equal(12, score.GamesWonBy(1));
        Assert.Equal(6, score.GamesWonBy(2));
    }

    [Fact]
    public void Der_Verlierer_kann_die_zweite_Seite_sein()
    {
        var score = Score.Played([Set(4, 6), Set(2, 6)], Standard);

        Assert.Equal(2, score.WinnerSide);
        Assert.Equal(1, score.LoserSide);
    }

    [Fact]
    public void Ein_Satz_mit_Tiebreak_ist_gueltig()
    {
        Assert.Equal("7:6 (5), 6:3", Score.Played([Set(7, 6, 5), Set(6, 3)], Standard).ToString());
    }

    [Fact]
    public void Ein_Match_Tiebreak_entscheidet_den_dritten_Satz()
    {
        var score = Score.Played([Set(6, 4), Set(3, 6), Set(10, 7)], Standard);

        Assert.Equal(1, score.WinnerSide);
        Assert.Equal(3, score.Sets.Count);
    }

    [Theory]
    [InlineData(9, 7)]
    [InlineData(10, 9)]
    [InlineData(13, 10)]
    public void Ein_ungueltiger_Match_Tiebreak_wird_abgewiesen(int a, int b)
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4), Set(3, 6), Set(a, b)], Standard));
    }

    [Theory]
    [InlineData(6, 6)]
    [InlineData(5, 3)]
    [InlineData(6, 5)]
    [InlineData(8, 5)]
    [InlineData(-1, 6)]
    public void Ein_ungueltiger_Satz_wird_abgewiesen(int a, int b)
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(a, b), Set(6, 0)], Standard));
    }

    [Fact]
    public void Mit_Tiebreak_endet_ein_Satz_spaetestens_7_6()
    {
        Score.Played([Set(7, 5), Set(7, 6, 2)], Standard);
        Assert.Throws<DomainException>(() => Score.Played([Set(8, 6), Set(6, 0)], Standard));
        Score.Played([Set(6, 4), Set(4, 6), Set(8, 6)], Advantage);
    }

    [Fact]
    public void Ein_Tiebreak_Ergebnis_gehoert_nur_zu_7_6()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4, 3), Set(6, 0)], Standard));
    }

    [Fact]
    public void Ein_unentschiedenes_Match_ist_kein_Ergebnis()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4)], Standard));
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4), Set(4, 6)], Standard));
    }

    [Fact]
    public void Ein_Satz_nach_der_Entscheidung_ist_einer_zu_viel()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4), Set(6, 4), Set(10, 8)], Standard));
    }

    [Fact]
    public void Im_Vorteilssatz_gibt_es_kein_7_6()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4), Set(4, 6), Set(7, 6, 4)], Advantage));
        Score.Played([Set(6, 4), Set(4, 6), Set(12, 10)], Advantage);
    }

    [Fact]
    public void Ein_Satz_genuegt_im_Einsatzformat()
    {
        Assert.Equal(2, Score.Played([Set(3, 6)], SingleSet).WinnerSide);

        // Auch mit Match-Tiebreak als Vorgabe: bei einem Satz ist der eine Satz ein Satz.
        var einSatzMitVorgabe = new MatchFormat(BestOf: 1);
        Assert.Equal(1, Score.Played([Set(7, 6, 4)], einSatzMitVorgabe).WinnerSide);
        Assert.Throws<DomainException>(() => Score.Played([Set(10, 8)], einSatzMitVorgabe));
        Score.Retired([], Set(5, 4), 2, einSatzMitVorgabe);
    }

    [Fact]
    public void Kurze_Saetze_gehen_bis_vier()
    {
        Score.Played([Set(4, 2), Set(5, 4, 3)], Kurz);
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4), Set(6, 4)], Kurz));
    }

    [Fact]
    public void Eine_Aufgabe_haelt_den_Stand_fest()
    {
        var score = Score.Retired([Set(6, 4)], Set(2, 1), retiringSide: 2, Standard);

        Assert.Equal(MatchOutcome.Retirement, score.Outcome);
        Assert.Equal(1, score.WinnerSide);
        Assert.Equal(1, score.SetsWonBy(1));
        Assert.Equal(8, score.GamesWonBy(1));
        Assert.Equal("6:4, 2:1 (Aufgabe)", score.ToString());
    }

    [Fact]
    public void Ein_entschiedenes_Match_laesst_sich_nicht_aufgeben()
    {
        Assert.Throws<DomainException>(() => Score.Retired([Set(6, 4), Set(6, 4)], null, 2, Standard));
    }

    [Fact]
    public void Ein_zu_Ende_gespielter_Satz_ist_kein_abgebrochener()
    {
        Assert.Throws<DomainException>(() => Score.Retired([Set(6, 4)], Set(6, 2), 2, Standard));
    }

    [Fact]
    public void Kampflos_und_Freilos_haben_keine_Saetze()
    {
        Assert.Equal(2, Score.Walkover(absentSide: 1).WinnerSide);
        Assert.Equal(1, Score.ByeFor(1).WinnerSide);
        Assert.Empty(Score.Walkover(1).Sets);
        Assert.Throws<DomainException>(() => Score.Walkover(3));
    }

    [Fact]
    public void Gleiche_Ergebnisse_sind_gleich()
    {
        Assert.Equal(Score.Played([Set(6, 4), Set(6, 2)], Standard), Score.Played([Set(6, 4), Set(6, 2)], Standard));
        Assert.NotEqual(Score.Played([Set(6, 4), Set(6, 2)], Standard), Score.Played([Set(6, 4), Set(6, 3)], Standard));
    }

    [Fact]
    public void Das_Format_beschreibt_sich()
    {
        Assert.Equal("2 Gewinnsätze, Match-Tiebreak statt des letzten Satzes", Standard.Describe());
        Assert.Equal("ein Satz", SingleSet.Describe());
        Assert.Equal("2 Gewinnsätze bis 4, Match-Tiebreak statt des letzten Satzes", Kurz.Describe());
        Assert.Throws<DomainException>(() => new MatchFormat(BestOf: 2).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Ein_Satz_geht_bis_eins_bis_zwoelf(int bis)
    {
        Assert.Throws<DomainException>(() => new MatchFormat(BestOf: 3, TiebreakAt: bis).Validate());
    }

    [Fact]
    public void Mehr_Saetze_als_das_Format_hergibt()
    {
        Assert.Throws<DomainException>(() =>
            Score.Played([Set(6, 4), Set(4, 6), Set(6, 4), Set(6, 4)], Advantage));
    }

    [Fact]
    public void Nach_dem_entscheidenden_Satz_kommt_keiner_mehr()
    {
        // Zwei Sätze gewonnen, danach steht noch ein dritter da.
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4), Set(6, 4), Set(4, 6)], Advantage));
    }

    [Fact]
    public void In_der_Verlaengerung_entscheiden_genau_zwei_Spiele()
    {
        Score.Played([Set(6, 4), Set(4, 6), Set(8, 6)], Advantage);
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4), Set(4, 6), Set(8, 5)], Advantage));
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(2, -1)]
    public void Ein_abgebrochener_Satz_hat_keine_negativen_Spiele(int a, int b)
    {
        Assert.Throws<DomainException>(() => Score.Retired([], Set(a, b), retiringSide: 2, Standard));
    }

    [Fact]
    public void Ein_abgebrochener_Satz_hat_kein_Tiebreak_Ergebnis()
    {
        Assert.Throws<DomainException>(() => Score.Retired([], Set(2, 1, 3), retiringSide: 2, Standard));
    }

    [Fact]
    public void Eine_Aufgabe_im_Match_Tiebreak()
    {
        // Beim Stand 1:1 läuft der Match-Tiebreak — darin darf aufgegeben werden,
        // solange er noch nicht gewonnen ist.
        var laufend = Score.Retired([Set(6, 4), Set(4, 6)], Set(5, 3), retiringSide: 2, Standard);
        Assert.Equal(1, laufend.WinnerSide);

        Assert.Throws<DomainException>(() =>
            Score.Retired([Set(6, 4), Set(4, 6)], Set(10, 3), retiringSide: 2, Standard));
    }

    [Fact]
    public void Wann_ein_abgebrochener_Satz_noch_laeuft()
    {
        // Mit Tiebreak: 6:5 läuft noch, 6:4 und 7:5 sind zu Ende gespielt.
        Score.Retired([], Set(6, 5), retiringSide: 2, Standard);
        Assert.Throws<DomainException>(() => Score.Retired([], Set(6, 4), retiringSide: 2, Standard));
        Assert.Throws<DomainException>(() => Score.Retired([], Set(7, 5), retiringSide: 2, Standard));

        // Im Vorteilssatz: 4:2 und 6:5 laufen noch, 8:6 ist zu Ende.
        Score.Retired([Set(6, 4), Set(4, 6)], Set(4, 2), retiringSide: 2, Advantage);
        Score.Retired([Set(6, 4), Set(4, 6)], Set(6, 5), retiringSide: 2, Advantage);
        Assert.Throws<DomainException>(() =>
            Score.Retired([Set(6, 4), Set(4, 6)], Set(8, 6), retiringSide: 2, Advantage));
    }

    [Fact]
    public void Ergebnisse_vergleichen_sich_ueber_alles_was_sie_ausmacht()
    {
        var score = Score.Played([Set(6, 4), Set(6, 2)], Standard);

        Assert.False(score.Equals(null));
        Assert.True(score.Equals(Score.Played([Set(6, 4), Set(6, 2)], Standard)));

        // Anderer Ausgang, andere Seite, anderer abgebrochener Satz.
        Assert.NotEqual(Score.Walkover(absentSide: 2), Score.ByeFor(1));
        Assert.NotEqual(Score.ByeFor(1), Score.ByeFor(2));
        Assert.NotEqual(
            Score.Retired([Set(6, 4)], Set(2, 1), retiringSide: 2, Standard),
            Score.Retired([Set(6, 4)], Set(3, 1), retiringSide: 2, Standard));
    }

    [Fact]
    public void Gleiche_Ergebnisse_liegen_im_selben_Fach()
    {
        var menge = new HashSet<Score>
        {
            Score.Played([Set(6, 4), Set(6, 2)], Standard),
            Score.Played([Set(6, 4), Set(6, 2)], Standard),
            Score.Played([Set(6, 4), Set(6, 3)], Standard),

            // Ohne Sätze: kampflos und Freilos sind zwei verschiedene Dinge.
            Score.Walkover(absentSide: 2),
            Score.ByeFor(1),
        };

        Assert.Equal(4, menge.Count);
    }

    [Fact]
    public void Jeder_Ausgang_schreibt_sich_anders()
    {
        Assert.Equal("6:4, 6:2", Score.Played([Set(6, 4), Set(6, 2)], Standard).ToString());
        Assert.Equal("kampflos", Score.Walkover(absentSide: 2).ToString());
        Assert.Equal("Freilos", Score.ByeFor(1).ToString());
        Assert.Equal("Aufgabe", Score.Retired([], null, retiringSide: 2, Standard).ToString());

        // Ein Ausgang, den es nicht gibt — so etwas kann nur aus der Datenbank kommen.
        Assert.Equal("99", Score.Rehydrate((MatchOutcome)99, 1, [], null).ToString());
    }
}
