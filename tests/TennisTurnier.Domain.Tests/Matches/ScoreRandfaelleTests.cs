using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Tests.Matches;

/// <summary>
/// Die Ränder des Ergebnisses: der abgebrochene Satz, die Seite, die es nicht
/// gibt, und wie ein Ergebnis sich nennt.
///
/// Der abgebrochene Satz ist der heikelste Teil. Er zählt für niemanden und
/// muss trotzdem möglich sein — ein zu Ende gespielter Satz gehört in die
/// andere Liste, sonst zählt ihn die Tabelle nicht mit.
/// </summary>
public sealed class ScoreRandfaelleTests
{
    private static readonly MatchFormat ZweiGewinnsaetze = new();

    private static readonly MatchFormat EinSatz =
        new(BestOf: 1, FinalSetMode: FinalSetMode.Regular, TiebreakAt: 4);

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Eine_Seite_ist_eins_oder_zwei(int seite)
    {
        Assert.Throws<DomainException>(() => Score.Walkover(seite));
        Assert.Throws<DomainException>(() => Score.Disqualified(seite));
        Assert.Throws<DomainException>(() => Score.ByeFor(seite));
        Assert.Throws<DomainException>(() => Score.Rehydrate(MatchOutcome.Normal, seite, [], null));
    }

    [Fact]
    public void Ein_entschiedenes_Match_laesst_sich_nicht_mehr_aufgeben()
    {
        // Wäre es entschieden, gäbe es nichts aufzugeben — und genau deshalb
        // kann hinter dem letzten Satz auch kein weiterer begonnen haben.
        var fehler = Assert.Throws<DomainException>(() =>
            Score.Retired(
                completedSets: [new SetScore(4, 2)],
                abandonedSet: new SetScore(1, 0),
                retiringSide: 2,
                format: EinSatz));

        Assert.Contains("nicht mehr aufgeben", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_abgebrochener_Satz_hat_keine_negativen_Spielstaende()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Score.Retired([], new SetScore(-1, 0), 1, ZweiGewinnsaetze));

        Assert.Contains("negative Spielstände", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_abgebrochener_Satz_hat_kein_Tiebreak_Ergebnis()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Score.Retired([], new SetScore(3, 2, TiebreakPoints: 5), 1, ZweiGewinnsaetze));

        Assert.Contains("kein Tiebreak-Ergebnis", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_zu_Ende_gespielter_Satz_gehoert_zu_den_gespielten()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Score.Retired([], new SetScore(6, 4), 2, ZweiGewinnsaetze));

        Assert.Contains("gehört zu den gespielten Sätzen", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_abgebrochener_Match_Tiebreak_zaehlt_bis_zehn()
    {
        // Im Entscheidungssatz gelten andere Grenzen: 7:5 ist dort noch offen,
        // in einem regulären Satz wäre es vorbei.
        var laufend = Score.Retired(
            completedSets: [new SetScore(6, 4), new SetScore(3, 6)],
            abandonedSet: new SetScore(7, 5),
            retiringSide: 1,
            format: ZweiGewinnsaetze);

        Assert.Equal(2, laufend.WinnerSide);

        var zuEnde = Assert.Throws<DomainException>(() =>
            Score.Retired(
                [new SetScore(6, 4), new SetScore(3, 6)],
                new SetScore(10, 5),
                1,
                ZweiGewinnsaetze));

        Assert.Contains("gehört zu den gespielten Sätzen", zuEnde.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Aufgabe_ohne_entschiedenen_Satz_gewinnt_die_andere_Seite()
    {
        // Niemand hat die nötigen Sätze — die Aufgabe entscheidet, nicht die
        // Zahl der Sätze.
        var ergebnis = Score.Retired(
            completedSets: [new SetScore(6, 4), new SetScore(2, 6)],
            abandonedSet: null,
            retiringSide: 1,
            format: ZweiGewinnsaetze);

        Assert.Equal(2, ergebnis.WinnerSide);
        Assert.Equal(1, ergebnis.LoserSide);
    }

    [Fact]
    public void Eine_Aufgabe_nennt_den_Stand_wenn_es_einen_gibt()
    {
        var ohneSatz = Score.Retired([], null, 1, ZweiGewinnsaetze);
        Assert.Equal("Aufgabe", ohneSatz.ToString());

        var mitSatz = Score.Retired([new SetScore(6, 4)], null, 2, ZweiGewinnsaetze);
        Assert.Equal("6:4 (Aufgabe)", mitSatz.ToString());
    }

    [Fact]
    public void Jeder_Ausgang_nennt_sich_verstaendlich()
    {
        Assert.Equal("6:4, 6:3", Score.Played([new SetScore(6, 4), new SetScore(6, 3)], ZweiGewinnsaetze).ToString());
        Assert.Equal("kampflos", Score.Walkover(2).ToString());
        Assert.Equal("Disqualifikation", Score.Disqualified(2).ToString());
        Assert.Equal("Freilos", Score.ByeFor(1).ToString());
    }

    [Fact]
    public void Ein_unbekannter_Ausgang_nennt_wenigstens_sich_selbst()
    {
        // Aus der Ablage kann ein Wert kommen, den diese Fassung nicht kennt —
        // etwa nach einem Rückbau. Eine leere Anzeige wäre schlimmer als der
        // rohe Name.
        var ergebnis = Score.Rehydrate((MatchOutcome)99, 1, [], null);

        Assert.Equal("99", ergebnis.ToString());
    }

    [Fact]
    public void Ein_wiederhergestelltes_Ergebnis_wird_nicht_erneut_geprueft()
    {
        // Die Regeln stehen im eingefrorenen Format des Turniers und sind beim
        // Laden eines einzelnen Matches nicht zur Hand. Sie zu erraten hieße,
        // ein gültiges Ergebnis unlesbar zu machen.
        var ergebnis = Score.Rehydrate(
            MatchOutcome.Normal,
            winnerSide: 1,
            completedSets: [new SetScore(5, 4)],
            abandonedSet: null);

        Assert.Equal(1, ergebnis.WinnerSide);
        Assert.Single(ergebnis.CompletedSets);
        Assert.Throws<ArgumentNullException>(() =>
            Score.Rehydrate(MatchOutcome.Normal, 1, null!, null));
    }

    [Fact]
    public void Der_abgebrochene_Satz_zaehlt_fuer_niemanden()
    {
        var ergebnis = Score.Retired(
            completedSets: [new SetScore(6, 4)],
            abandonedSet: new SetScore(3, 2),
            retiringSide: 2,
            format: ZweiGewinnsaetze);

        Assert.Equal(1, ergebnis.SetsWonBy(1));
        Assert.Equal(0, ergebnis.SetsWonBy(2));
        Assert.Equal(1, ergebnis.WinnerSide);
    }
}
