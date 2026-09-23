using Matchday.Domain;

namespace Matchday.Domain.Tests;

public sealed class LiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    /// <summary>„1“ und „2“ sind Punkte, „a“ und „b“ ganze Spiele für Seite 1 und 2.</summary>
    private static List<LiveEvent> Folge(string text) =>
        text.Where(c => c != ' ').Select(c => c switch
        {
            '1' => new LiveEvent(LiveEventKind.Point, 1),
            '2' => new LiveEvent(LiveEventKind.Point, 2),
            'a' => new LiveEvent(LiveEventKind.Game, 1),
            _ => new LiveEvent(LiveEventKind.Game, 2),
        }).ToList();

    private static LiveState Stand(string text, MatchFormat? format = null) =>
        LiveScoring.Replay(Folge(text), format ?? MatchFormat.Standard);

    private static string Spiele(int games, char side) => new(side, games);

    /// <summary>Abwechselnd je ein Spiel — n:n, ohne dass unterwegs ein Satz endet.</summary>
    private static string Gleich(int games) => string.Concat(Enumerable.Repeat("ab", games));

    [Fact]
    public void Ein_Spiel_wird_gezaehlt_wie_man_es_ansagt()
    {
        Assert.Equal(("0", "0"), Ansage(Stand("")));
        Assert.Equal(("15", "0"), Ansage(Stand("1")));
        Assert.Equal(("30", "15"), Ansage(Stand("112")));
        Assert.Equal(("40", "30"), Ansage(Stand("11122")));
        Assert.Equal(("40", "40"), Ansage(Stand("111222")));
        Assert.Equal(("A", "40"), Ansage(Stand("1112221")));
        Assert.Equal(("40", "A"), Ansage(Stand("11122212 2")));

        // Nach Vorteil und dem nächsten Punkt ist das Spiel weg.
        var spiel = Stand("11122211");
        Assert.Equal((1, 0), (spiel.Games1, spiel.Games2));
        Assert.Equal(("0", "0"), Ansage(spiel));

        Assert.Equal((0, 1), (Stand("2222").Games1, Stand("2222").Games2));

        static (string, string) Ansage(LiveState s) => (s.PointsText(1), s.PointsText(2));
    }

    [Fact]
    public void Ein_ganzes_Spiel_laesst_die_Punkte_weg()
    {
        var s = Stand("11 b");
        Assert.Equal((0, 1), (s.Games1, s.Games2));
        Assert.Equal((0, 0), (s.Points1, s.Points2));
    }

    [Fact]
    public void Ein_Satz_endet_mit_zwei_Spielen_Vorsprung_oder_im_Tiebreak()
    {
        var sechsVier = Stand(Spiele(4, 'b') + Spiele(6, 'a'));
        Assert.Equal([new SetScore(6, 4)], sechsVier.CompletedSets);

        var siebenFuenf = Stand(Spiele(5, 'a') + Spiele(5, 'b') + "aa");
        Assert.Equal([new SetScore(7, 5)], siebenFuenf.CompletedSets);

        var tiebreak = Stand(Gleich(6));
        Assert.True(tiebreak.InTiebreak);
        Assert.Empty(tiebreak.CompletedSets);

        // Im Tiebreak zählen Punkte als Zahlen, und ein „Spiel“ ist ein Punkt.
        var laufend = Stand(Gleich(6) + "1a2");
        Assert.Equal(("2", "1"), (laufend.PointsText(1), laufend.PointsText(2)));

        var sieben = Stand(Gleich(6) + "22222" + "111111" + "1");
        Assert.Equal([new SetScore(7, 6, 5)], sieben.CompletedSets);
        Assert.False(sieben.InTiebreak);

        var fuerZwei = Stand(Gleich(6) + "2222222");
        Assert.Equal([new SetScore(6, 7, 0)], fuerZwei.CompletedSets);
    }

    [Fact]
    public void Das_Match_endet_mit_den_noetigen_Saetzen()
    {
        var zweiSaetze = Stand(Spiele(6, 'a') + Spiele(6, 'a'), new MatchFormat(BestOf: 3, FinalSetMode.Regular));
        Assert.Equal(1, zweiSaetze.WinnerSide);
        Assert.True(zweiSaetze.IsDecided);

        var fuerZwei = Stand(Spiele(6, 'b'), new MatchFormat(BestOf: 1));
        Assert.Equal(2, fuerZwei.WinnerSide);

        Assert.Contains("entschieden", Assert.Throws<DomainException>(() => Stand(Spiele(6, 'b') + "1", new MatchFormat(BestOf: 1))).Message);
    }

    [Fact]
    public void Der_Match_Tiebreak_geht_bis_zehn_mit_zwei_Vorsprung()
    {
        var bisDahin = Spiele(6, 'a') + Spiele(6, 'b');
        var s = Stand(bisDahin);
        Assert.True(s.InMatchTiebreak);

        var zehnAcht = Stand(bisDahin + "22222222" + "1111111111");
        Assert.Equal(new SetScore(10, 8), zehnAcht.CompletedSets[^1]);
        Assert.Equal(1, zehnAcht.WinnerSide);
        Assert.False(zehnAcht.InMatchTiebreak);

        var laufend = Stand(bisDahin + "111111111" + "222222222");
        Assert.Null(laufend.WinnerSide);
        Assert.Equal(("9", "9"), (laufend.PointsText(1), laufend.PointsText(2)));
    }

    [Fact]
    public void Ohne_Tiebreak_wird_der_letzte_Satz_durchgespielt()
    {
        var format = new MatchFormat(BestOf: 3, FinalSetMode.Advantage);
        var bisDahin = Spiele(6, 'a') + Spiele(6, 'b');

        var sechsSechs = Stand(bisDahin + Gleich(6), format);
        Assert.False(sechsSechs.InTiebreak);
        Assert.Null(sechsSechs.WinnerSide);

        var achtSechs = Stand(bisDahin + Gleich(6) + "aa", format);
        Assert.Equal(new SetScore(8, 6), achtSechs.CompletedSets[^1]);
        Assert.Equal(1, achtSechs.WinnerSide);
    }

    [Fact]
    public void Kurze_Saetze_gehen_bis_vier()
    {
        var format = new MatchFormat(BestOf: 1, FinalSetMode.Regular, TiebreakAt: 4);
        Assert.Equal([new SetScore(4, 2)], Stand(Spiele(2, 'b') + Spiele(4, 'a'), format).CompletedSets);
        Assert.True(Stand(Gleich(4), format).InTiebreak);
        Assert.Equal([new SetScore(5, 4, 3)], Stand(Gleich(4) + "222" + "1111111", format).CompletedSets);
    }

    [Fact]
    public void Eine_Seite_ist_eins_oder_zwei()
    {
        Assert.Throws<DomainException>(() => LiveScoring.Replay([new LiveEvent(LiveEventKind.Point, 3)], MatchFormat.Standard));
    }

    // --- Am Turnier -------------------------------------------------------

    private static (Tournament T, Match M) Gelost(MatchFormat? format = null, params string[] names)
    {
        var t = Tournament.Create("Live", "browser-1", Now, Mode.Knockout, format ?? new MatchFormat(BestOf: 1));

        foreach (var name in names.Length == 0 ? ["A", "B", "C", "D"] : names)
        {
            t.AddParticipant(name);
        }

        t.Draw(new Random(1));
        t.Start(Now);
        return (t, t.Matches.First(m => m.Status == MatchStatus.Ready));
    }

    private static void Zaehle(Tournament t, Match m, string text)
    {
        foreach (var e in Folge(text))
        {
            t.ScoreLive(m.Id, e);
        }
    }

    [Fact]
    public void Wer_live_zaehlt_bekommt_am_Ende_das_Ergebnis()
    {
        var (t, m) = Gelost();

        Zaehle(t, m, "1");
        Assert.Equal(MatchStatus.Playing, m.Status);
        Assert.Equal("15", t.LiveStateOf(m)!.PointsText(1));
        Assert.True(t.IsStarted);

        Zaehle(t, m, "11" + "1" + Spiele(5, 'a'));
        Assert.Equal(MatchStatus.Finished, m.Status);
        Assert.Equal([new SetScore(6, 0)], m.Score!.Sets);
        Assert.NotNull(m.Live);

        // Der Sieger ist ins Finale gerückt.
        var finale = t.Matches.Single(x => x.Round == 2);
        Assert.Contains(m.WinnerId, new[] { finale.Side1.ParticipantId, finale.Side2.ParticipantId });

        Assert.Contains("entschieden", Assert.Throws<DomainException>(() => t.ScoreLive(m.Id, new LiveEvent(LiveEventKind.Point, 1))).Message);
    }

    [Fact]
    public void Rueckgaengig_nimmt_auch_den_entscheidenden_Punkt_zurueck()
    {
        var (t, m) = Gelost();
        Zaehle(t, m, Spiele(6, 'a'));
        Assert.Equal(MatchStatus.Finished, m.Status);

        var stand = t.UndoLive(m.Id);
        Assert.Null(m.Score);
        Assert.Equal(MatchStatus.Playing, m.Status);
        Assert.Equal((5, 0), (stand.Games1, stand.Games2));
        Assert.Equal(TournamentState.Running, t.State);

        // Bis zum Anfang zurück — dann zählt niemand mehr mit.
        for (var i = 0; i < 5; i++)
        {
            t.UndoLive(m.Id);
        }

        Assert.Null(m.Live);
        Assert.Null(t.LiveStateOf(m));
        Assert.Equal(MatchStatus.Ready, m.Status);
        Assert.Contains("nichts live", Assert.Throws<DomainException>(() => t.UndoLive(m.Id)).Message);
    }

    [Fact]
    public void Rueckgaengig_haelt_an_einem_Folgematch_mit_Ergebnis()
    {
        var (t, m) = Gelost();
        Zaehle(t, m, Spiele(6, 'a'));
        var anderes = t.Matches.First(x => x.Status == MatchStatus.Ready && x.Round == 1);
        t.RecordResult(anderes.Id, Score.Played([new(6, 1)], t.Format));
        var finale = t.Matches.Single(x => x.Round == 2);
        t.RecordResult(finale.Id, Score.Played([new(6, 1)], t.Format));

        Assert.Contains("Folgematch", Assert.Throws<DomainException>(() => t.UndoLive(m.Id)).Message);
        Assert.NotNull(m.Score);
        Assert.Equal(6, m.Live!.Count);
    }

    [Fact]
    public void Ein_eingetragenes_Ergebnis_ersetzt_das_Mitzaehlen()
    {
        var (t, m) = Gelost();
        Zaehle(t, m, "11a");

        t.RecordResult(m.Id, Score.Played([new(6, 3)], t.Format));
        Assert.Null(m.Live);
        Assert.Contains("schon ein Ergebnis", Assert.Throws<DomainException>(() => t.ScoreLive(m.Id, new LiveEvent(LiveEventKind.Point, 1))).Message);

        // Wer das Ergebnis löscht, fängt beim Zählen von vorn an.
        t.ClearResult(m.Id);
        Zaehle(t, m, Spiele(6, 'b'));
        Assert.Equal(2, m.Score!.WinnerSide);
        t.ClearResult(m.Id);
        Assert.Null(m.Live);
        Assert.Equal(MatchStatus.Ready, m.Status);
    }

    [Fact]
    public void Live_gibt_es_nur_fuer_Matches_mit_zwei_Gegnern()
    {
        var (t, _) = Gelost(null, "A", "B", "C");
        var freilos = t.Matches.First(x => x.IsBye);
        var finale = t.Matches.Single(x => x.Round == 2);

        Assert.Contains("Freilos", Assert.Throws<DomainException>(() => t.ScoreLive(freilos.Id, new LiveEvent(LiveEventKind.Point, 1))).Message);
        Assert.Contains("Gegner", Assert.Throws<DomainException>(() => t.ScoreLive(finale.Id, new LiveEvent(LiveEventKind.Point, 1))).Message);

        var offen = Tournament.Create("Offen", "browser-1", Now);
        Assert.Throws<DomainException>(() => offen.ScoreLive(Guid.NewGuid(), new LiveEvent(LiveEventKind.Point, 1)));
        Assert.Throws<DomainException>(() => offen.UndoLive(Guid.NewGuid()));
    }

    [Fact]
    public void Der_Stand_uebersteht_den_Speicher()
    {
        var (t, m) = Gelost();
        Zaehle(t, m, "112a");

        var copy = Tournament.FromSnapshot(t.ToSnapshot());
        var wieder = copy.FindMatch(m.Id);

        Assert.Equal(m.Live, wieder.Live);
        Assert.Equal(MatchStatus.Playing, wieder.Status);
        Assert.Equal(t.LiveStateOf(m)!.Games1, copy.LiveStateOf(wieder)!.Games1);
    }

    [Fact]
    public void Der_Eintragen_Link_folgt_dem_Verwalterlink()
    {
        var t = Tournament.Create("Link", "browser-1", Now);
        var scorer = t.ScorerToken;

        Assert.Equal(scorer, Tournament.FromSnapshot(t.ToSnapshot()).ScorerToken);
        Assert.NotEqual(t.AdminToken, scorer);
        Assert.True(scorer.Length >= 20);
        Assert.DoesNotContain('=', scorer);

        t.RotateAdminToken();
        Assert.NotEqual(scorer, t.ScorerToken);
    }
}
