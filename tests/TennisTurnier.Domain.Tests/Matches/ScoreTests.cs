using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Tests.Matches;

public sealed class ScoreTests
{
    /// <summary>Zwei Gewinnsätze, Match-Tiebreak im dritten — das übliche Vereinsformat.</summary>
    private static readonly MatchFormat Standard = new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6);

    /// <summary>Drei volle Sätze, Entscheidungssatz ohne Tiebreak.</summary>
    private static readonly MatchFormat Advantage = new(BestOf: 3, FinalSetMode.Advantage, TiebreakAt: 6);

    private static readonly MatchFormat SingleSet = new(BestOf: 1, FinalSetMode.Regular, TiebreakAt: 6);

    private static SetScore Set(int a, int b, int? tiebreak = null) => new(a, b, tiebreak);

    [Fact]
    public void Ein_glatter_Zweisatzsieg_ist_gueltig()
    {
        var score = Score.Played([Set(6, 4), Set(6, 2)], Standard);

        Assert.Equal(MatchOutcome.Normal, score.Outcome);
        Assert.Equal(1, score.WinnerSide);
        Assert.Equal(2, score.SetsWonBy(1));
        Assert.Equal(0, score.SetsWonBy(2));
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
        var score = Score.Played([Set(7, 6, 5), Set(6, 3)], Standard);

        Assert.Equal("7:6 (5), 6:3", score.ToString());
    }

    [Fact]
    public void Ein_Match_Tiebreak_entscheidet_den_dritten_Satz()
    {
        var score = Score.Played([Set(6, 4), Set(3, 6), Set(10, 7)], Standard);

        Assert.Equal(1, score.WinnerSide);
        Assert.Equal(3, score.Sets.Count);
    }

    [Fact]
    public void Ein_verlaengerter_Match_Tiebreak_ist_gueltig()
    {
        Score.Played([Set(6, 4), Set(3, 6), Set(13, 11)], Standard);
    }

    [Fact]
    public void Ein_Match_Tiebreak_unter_zehn_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4), Set(3, 6), Set(9, 7)], Standard));
    }

    [Fact]
    public void Ein_Match_Tiebreak_ohne_zwei_Punkte_Vorsprung_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4), Set(3, 6), Set(10, 9)], Standard));
    }

    [Fact]
    public void Ein_verlaengerter_Match_Tiebreak_mit_drei_Punkten_Vorsprung_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4), Set(3, 6), Set(13, 10)], Standard));
    }

    [Fact]
    public void Im_Advantage_Format_wird_der_Entscheidungssatz_ausgespielt()
    {
        Score.Played([Set(6, 4), Set(3, 6), Set(8, 6)], Advantage);
    }

    [Fact]
    public void Im_Advantage_Format_gibt_es_im_Entscheidungssatz_keinen_Tiebreak()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4), Set(3, 6), Set(7, 6, 5)], Advantage));
    }

    [Fact]
    public void Ein_unentschiedener_Satz_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 6), Set(6, 2)], Standard));
    }

    [Fact]
    public void Negative_Spielstaende_sind_ungueltig()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(-1, 6), Set(6, 2)], Standard));
    }

    [Fact]
    public void Ein_zu_kurzer_Satz_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(5, 3), Set(6, 2)], Standard));
    }

    [Fact]
    public void Ein_Satz_bei_sechs_ohne_zwei_Spiele_Vorsprung_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 5), Set(6, 2)], Standard));
    }

    [Fact]
    public void Eine_Verlaengerung_mit_drei_Spielen_Vorsprung_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(8, 5), Set(6, 2)], Advantage));
    }

    [Fact]
    public void Ein_Tiebreak_Ergebnis_an_einem_normalen_Satz_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4, 5), Set(6, 2)], Standard));
    }

    [Fact]
    public void Zu_viele_Saetze_sind_ungueltig()
    {
        Assert.Throws<DomainException>(
            () => Score.Played([Set(6, 4), Set(6, 4), Set(6, 4), Set(6, 4)], Standard));
    }

    [Fact]
    public void Ein_Satz_nach_der_Entscheidung_ist_ungueltig()
    {
        // 6:4, 6:4 entscheidet das Match. Ein dritter Satz kann nicht gespielt
        // worden sein — hier wurde entweder falsch eingetragen oder das Format
        // stimmt nicht.
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4), Set(6, 4), Set(6, 4)], Standard));
    }

    [Fact]
    public void Ein_unvollstaendiges_Match_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => Score.Played([Set(6, 4)], Standard));
    }

    [Fact]
    public void Ein_Satz_genuegt_wenn_das_Format_es_so_will()
    {
        var score = Score.Played([Set(6, 4)], SingleSet);

        Assert.Equal(1, score.WinnerSide);
    }

    [Fact]
    public void Bei_einer_Aufgabe_gewinnt_die_andere_Seite()
    {
        var score = Score.Retired([Set(6, 4)], Set(2, 1), retiringSide: 2, Standard);

        Assert.Equal(MatchOutcome.Retirement, score.Outcome);
        Assert.Equal(1, score.WinnerSide);
    }

    [Fact]
    public void Der_abgebrochene_Satz_zaehlt_fuer_niemanden()
    {
        // Läge er in derselben Liste wie die gespielten Sätze, wiese die Tabelle
        // einen Satz aus, der nie zu Ende gespielt wurde.
        var score = Score.Retired([Set(6, 4)], Set(2, 1), retiringSide: 2, Standard);

        Assert.Equal(1, score.SetsWonBy(1));
        Assert.Equal(0, score.SetsWonBy(2));
    }

    [Fact]
    public void Die_Spiele_des_abgebrochenen_Satzes_zaehlen_mit()
    {
        // Sie wurden gespielt — anders als beim Nichtantreten, wo es gar keinen
        // Spielstand gibt.
        var score = Score.Retired([Set(6, 4)], Set(2, 1), retiringSide: 2, Standard);

        Assert.Equal(8, score.GamesWonBy(1));
        Assert.Equal(5, score.GamesWonBy(2));
    }

    [Fact]
    public void Eine_Aufgabe_kann_im_ersten_Satz_erfolgen()
    {
        var score = Score.Retired([], Set(3, 2), retiringSide: 1, Standard);

        Assert.Equal(2, score.WinnerSide);
        Assert.Equal("3:2 (Aufgabe)", score.ToString());
    }

    [Fact]
    public void Eine_Aufgabe_vor_dem_ersten_Spiel_ist_moeglich()
    {
        var score = Score.Retired([], abandonedSet: null, retiringSide: 1, Standard);

        Assert.Equal(2, score.WinnerSide);
        Assert.Empty(score.Sets);
    }

    [Fact]
    public void Ein_bereits_entschiedenes_Match_laesst_sich_nicht_aufgeben()
    {
        // Wäre es entschieden, gäbe es nichts mehr aufzugeben — hier wurde
        // vermutlich der falsche Ausgang gewählt.
        Assert.Throws<DomainException>(
            () => Score.Retired([Set(6, 4), Set(6, 4)], null, retiringSide: 2, Standard));
    }

    [Fact]
    public void Ein_zu_Ende_gespielter_Satz_gehoert_nicht_in_den_Abbruch()
    {
        Assert.Throws<DomainException>(
            () => Score.Retired([], Set(6, 4), retiringSide: 1, Standard));
    }

    [Fact]
    public void Auch_bei_einer_Aufgabe_muessen_die_gespielten_Saetze_moeglich_sein()
    {
        Assert.Throws<DomainException>(
            () => Score.Retired([Set(6, 6)], null, retiringSide: 1, Standard));
    }

    [Fact]
    public void Ein_abgebrochener_Satz_hat_kein_Tiebreak_Ergebnis()
    {
        Assert.Throws<DomainException>(
            () => Score.Retired([], Set(3, 2, 5), retiringSide: 1, Standard));
    }

    [Fact]
    public void Ein_unentschiedener_Stand_im_abgebrochenen_Satz_ist_zulaessig()
    {
        // 2:2 ist ein völlig normaler Zwischenstand.
        var score = Score.Retired([], Set(2, 2), retiringSide: 1, Standard);

        Assert.Equal(2, score.GamesWonBy(1));
        Assert.Equal(2, score.GamesWonBy(2));
    }

    [Fact]
    public void Nichtantreten_hat_keinen_Spielstand()
    {
        var score = Score.Walkover(absentSide: 1);

        Assert.Equal(MatchOutcome.Walkover, score.Outcome);
        Assert.Equal(2, score.WinnerSide);
        Assert.Empty(score.Sets);
        Assert.Equal(0, score.GamesWonBy(2));
    }

    [Fact]
    public void Eine_Disqualifikation_gibt_der_anderen_Seite_den_Sieg()
    {
        var score = Score.Disqualified(disqualifiedSide: 2);

        Assert.Equal(MatchOutcome.Disqualification, score.Outcome);
        Assert.Equal(1, score.WinnerSide);
    }

    [Fact]
    public void Ein_Freilos_bringt_die_angegebene_Seite_weiter()
    {
        var score = Score.ByeFor(advancingSide: 2);

        Assert.Equal(MatchOutcome.Bye, score.Outcome);
        Assert.Equal(2, score.WinnerSide);
        Assert.Empty(score.Sets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Eine_Seite_ist_eins_oder_zwei(int side)
    {
        Assert.Throws<DomainException>(() => Score.Walkover(side));
        Assert.Throws<DomainException>(() => Score.ByeFor(side));
        Assert.Throws<DomainException>(() => Score.Disqualified(side));
    }

    [Fact]
    public void Ein_Tiebreak_bei_abweichendem_Zielstand_folgt_dem_Format()
    {
        // Kurzsatz bis 4 mit Tiebreak bei 4:4 — im Jugendbereich verbreitet.
        var shortSets = new MatchFormat(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 4);

        Score.Played([Set(5, 4, 3), Set(4, 1)], shortSets);
        Assert.Throws<DomainException>(() => Score.Played([Set(7, 6, 5), Set(4, 1)], shortSets));
    }

    [Fact]
    public void Zwei_inhaltsgleiche_Ergebnisse_sind_gleich()
    {
        // Der vom Record erzeugte Vergleich prüft die Satzliste über ihre
        // Referenz und hielte diese beiden für verschieden. Die
        // Änderungsverfolgung der Persistenz baut darauf auf: sie schriebe dann
        // ein unverändertes Ergebnis erneut in die Datenbank.
        var a = Score.Played([Set(6, 4), Set(7, 6, 5)], Standard);
        var b = Score.Played([Set(6, 4), Set(7, 6, 5)], Standard);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Ein_abweichender_abgebrochener_Satz_macht_zwei_Ergebnisse_verschieden()
    {
        // Die Gegenprobe: ohne sie wäre die Gleichheit oben auch dann erfüllt,
        // wenn sie schlicht alles für gleich hielte.
        var a = Score.Retired([Set(6, 4)], Set(2, 1), retiringSide: 2, Standard);
        var b = Score.Retired([Set(6, 4)], Set(3, 1), retiringSide: 2, Standard);

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, Score.Retired([Set(6, 4)], null, retiringSide: 2, Standard));
    }
}
