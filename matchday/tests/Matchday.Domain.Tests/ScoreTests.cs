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
}
