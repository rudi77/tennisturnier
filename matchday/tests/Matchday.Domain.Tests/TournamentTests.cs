using Matchday.Domain;

namespace Matchday.Domain.Tests;

public sealed class TournamentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static Tournament Neu(Mode mode, params string[] names)
    {
        var t = Tournament.Create("Clubmeisterschaft", "browser-1", Now, mode);
        foreach (var name in names)
        {
            t.AddParticipant(name);
        }

        return t;
    }

    private static Score Sieg1(Tournament t) => Score.Played([new(6, 4), new(6, 4)], t.Format);

    private static Score Sieg2(Tournament t) => Score.Played([new(4, 6), new(4, 6)], t.Format);

    [Fact]
    public void Ein_neues_Turnier_hat_Token_und_Standardformat()
    {
        var t = Tournament.Create("  Sommercup ", "browser-1", Now, location: " Baden ");

        Assert.Equal("Sommercup", t.Name);
        Assert.Equal("Baden", t.Location);
        Assert.Equal(TournamentState.Setup, t.State);
        Assert.Equal(MatchFormat.Standard, t.Format);
        Assert.True(t.AdminToken.Length >= 20);
        Assert.Throws<DomainException>(() => Tournament.Create(" ", "browser-1", Now));
    }

    [Fact]
    public void Teilnehmer_sind_Namen_und_eindeutig()
    {
        var t = Neu(Mode.Knockout, "Rudi", "Max");

        Assert.Throws<DomainException>(() => t.AddParticipant("rudi"));
        Assert.Throws<DomainException>(() => t.AddParticipant(""));
        Assert.Equal("Rudi", t.FindParticipant("RUDI")!.Name);
        Assert.Equal("Max", t.FindParticipant("ma")!.Name);

        t.RemoveParticipant(t.Participants[0].Id);
        Assert.Single(t.Participants);
        Assert.Throws<DomainException>(() => t.RemoveParticipant(Guid.NewGuid()));
    }

    [Fact]
    public void Auslosen_braucht_zwei_Teilnehmer()
    {
        var t = Neu(Mode.Knockout, "Rudi");
        Assert.Throws<DomainException>(() => t.Draw());
        Assert.Throws<DomainException>(() => t.UndoDraw());
    }

    [Fact]
    public void Ein_Ko_Turnier_mit_vier_Spielern_laeuft_bis_zum_Finale()
    {
        var t = Neu(Mode.Knockout, "A", "B", "C", "D");
        t.Draw(new Random(1));

        Assert.Equal(TournamentState.Running, t.State);
        Assert.Equal(3, t.Matches.Count);
        Assert.Equal(["Halbfinale 1", "Halbfinale 2", "Finale"], t.Matches.Select(m => m.Label));
        Assert.All(t.Matches.Where(m => m.Round == 1), m => Assert.Equal(MatchStatus.Ready, m.Status));
        Assert.Equal(MatchStatus.Pending, t.Matches[2].Status);

        Assert.Throws<DomainException>(() => t.RecordResult(t.Matches[2].Id, Sieg1(t)));

        t.RecordResult(t.Matches[0].Id, Sieg1(t));
        t.RecordResult(t.Matches[1].Id, Sieg2(t));

        var final = t.Matches[2];
        Assert.Equal(MatchStatus.Ready, final.Status);
        Assert.Equal(t.Matches[0].WinnerId, final.Side1.ParticipantId);
        Assert.Equal(t.Matches[1].WinnerId, final.Side2.ParticipantId);

        t.RecordResult(final.Id, Sieg1(t));
        Assert.Equal(TournamentState.Completed, t.State);

        var standings = t.Standings();
        Assert.Equal(final.WinnerId, standings[0].ParticipantId);
        Assert.Equal(1, standings[0].Rank);
        Assert.Equal(2, standings[1].Rank);
        Assert.Equal([3, 3], standings.Skip(2).Select(s => s.Rank));
    }

    [Fact]
    public void Teilnehmer_werden_gemischt()
    {
        var t = Neu(Mode.Knockout, "A", "B", "C", "D", "E", "F", "G", "H");
        t.Draw(new Random(7));

        var firstRound = t.Matches.Where(m => m.Round == 1).Select(m => t.NameOf(m.Side1) + t.NameOf(m.Side2));
        Assert.NotEqual(["AB", "CD", "EF", "GH"], firstRound);
    }

    [Fact]
    public void Freilose_sind_beim_Auslosen_entschieden()
    {
        var t = Neu(Mode.Knockout, "A", "B", "C", "D", "E");
        t.Draw(new Random(3));

        Assert.Equal(7, t.Matches.Count);
        var byes = t.Matches.Where(m => m.IsBye).ToList();
        Assert.Equal(3, byes.Count);
        Assert.All(byes, m => Assert.Equal(MatchOutcome.Bye, m.Score!.Outcome));
        Assert.All(byes, m => Assert.NotNull(m.WinnerId));

        // Wer ein Freilos hat, steht schon im Halbfinale.
        var semis = t.Matches.Where(m => m.Round == 2).ToList();
        Assert.Equal(3, semis.Sum(m => (m.Side1.IsSettled ? 1 : 0) + (m.Side2.IsSettled ? 1 : 0)));

        Assert.Throws<DomainException>(() => t.ClearResult(byes[0].Id));
        Assert.Throws<DomainException>(() => t.RecordResult(byes[0].Id, Sieg1(t)));
        Assert.Equal(TournamentState.Running, t.State);
    }

    [Fact]
    public void Ein_Ergebnis_laesst_sich_zuruecknehmen_solange_nichts_darauf_aufbaut()
    {
        var t = Neu(Mode.Knockout, "A", "B", "C", "D");
        t.Draw(new Random(1));
        var (hf1, hf2, final) = (t.Matches[0], t.Matches[1], t.Matches[2]);

        t.RecordResult(hf1.Id, Sieg1(t));
        t.RecordResult(hf2.Id, Sieg1(t));
        t.RecordResult(final.Id, Sieg2(t));
        Assert.Equal(TournamentState.Completed, t.State);

        Assert.Throws<DomainException>(() => t.ClearResult(hf1.Id));
        Assert.Throws<DomainException>(() => t.RecordResult(hf1.Id, Sieg2(t)));

        t.ClearResult(final.Id);
        Assert.Equal(TournamentState.Running, t.State);
        t.RecordResult(hf1.Id, Sieg2(t));
        Assert.Equal(hf1.WinnerId, final.Side1.ParticipantId);

        t.ClearResult(hf1.Id);
        Assert.Equal(SideKind.WinnerOf, final.Side1.Kind);
        Assert.Equal(MatchStatus.Pending, final.Status);
        Assert.Throws<DomainException>(() => t.ClearResult(hf1.Id));
    }

    [Fact]
    public void Jeder_gegen_jeden_paart_alle_genau_einmal()
    {
        var t = Neu(Mode.RoundRobin, "A", "B", "C", "D", "E");
        t.Draw(new Random(1));

        Assert.Equal(10, t.Matches.Count);
        Assert.All(t.Matches, m => Assert.Equal(MatchStatus.Ready, m.Status));

        var pairs = t.Matches
            .Select(m => string.Join("|", new[] { t.NameOf(m.Side1), t.NameOf(m.Side2) }.Order()))
            .ToList();
        Assert.Equal(10, pairs.Distinct().Count());
        Assert.Equal(5, t.Matches.Max(m => m.Round));

        // Niemand spielt zweimal in einer Runde.
        foreach (var round in t.Matches.GroupBy(m => m.Round))
        {
            var names = round.SelectMany(m => new[] { m.Side1.ParticipantId, m.Side2.ParticipantId }).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }

    [Fact]
    public void Die_Tabelle_zaehlt_Siege_dann_Saetze_dann_Spiele()
    {
        var t = Neu(Mode.RoundRobin, "A", "B", "C");
        t.Draw(new Random(1));

        var ab = Find(t, "A", "B");
        var ac = Find(t, "A", "C");
        var bc = Find(t, "B", "C");

        Gewinnt(t, ab, "A", [new(6, 0), new(6, 0)]);
        Gewinnt(t, ac, "C", [new(6, 4), new(6, 4)]);
        Gewinnt(t, bc, "B", [new(7, 6, 3), new(6, 7, 3), new(10, 8)]);

        Assert.Equal(TournamentState.Completed, t.State);
        var table = t.Standings();

        // Alle ein Sieg. C: Sätze 3:2, A: 2:2 mit +8 Spielen, B: 2:3.
        Assert.All(table, s => Assert.Equal(1, s.Won));
        Assert.Equal(["C", "A", "B"], table.Select(s => s.Name));
        Assert.Equal([1, 2, 3], table.Select(s => s.Rank));
        Assert.Equal(1, table[0].SetDifference);
        Assert.Equal(0, table[1].SetDifference);
        Assert.Equal(8, table[1].GameDifference);
    }

    [Fact]
    public void Kampflos_zaehlt_als_Sieg_ohne_Saetze()
    {
        var t = Neu(Mode.RoundRobin, "A", "B");
        t.Draw(new Random(1));
        var m = t.Matches[0];

        t.RecordResult(m.Id, Score.Walkover(absentSide: 2));
        var table = t.Standings();

        Assert.Equal(m.Side1.ParticipantId, table[0].ParticipantId);
        Assert.Equal(1, table[0].Won);
        Assert.Equal(0, table[0].SetsWon);
    }

    [Fact]
    public void Nach_der_Auslosung_ist_der_Rahmen_eingefroren()
    {
        var t = Neu(Mode.Knockout, "A", "B");
        t.Draw(new Random(1));

        Assert.Throws<DomainException>(() => t.AddParticipant("C"));
        Assert.Throws<DomainException>(() => t.SetMode(Mode.RoundRobin));
        Assert.Throws<DomainException>(() => t.SetFormat(new MatchFormat(BestOf: 1)));
        t.Rename("Neuer Name");
        t.SetDate(new DateOnly(2026, 10, 3));

        t.UndoDraw();
        Assert.Equal(TournamentState.Setup, t.State);
        Assert.Empty(t.Matches);
        t.AddParticipant("C");
        t.SetMode(Mode.RoundRobin);
    }

    [Fact]
    public void Der_Schnappschuss_ist_verlustfrei()
    {
        var t = Neu(Mode.Knockout, "A", "B", "C");
        t.Draw(new Random(1));
        var ready = t.Matches.First(m => m.Status == MatchStatus.Ready);
        t.RecordResult(ready.Id, Score.Retired([new(6, 4)], new(3, 2), 2, t.Format));

        var copy = Tournament.FromSnapshot(t.ToSnapshot());

        Assert.Equal(t.Id, copy.Id);
        Assert.Equal(t.AdminToken, copy.AdminToken);
        Assert.Equal(t.Participants, copy.Participants);
        Assert.Equal(t.Matches.Select(m => (m.Id, m.Side1, m.Side2, m.Score)), copy.Matches.Select(m => (m.Id, m.Side1, m.Side2, m.Score)));
        Assert.Equal(t.State, copy.State);
        Assert.Equal(t.Standings(), copy.Standings());
    }

    [Fact]
    public void Ein_Match_beschreibt_sich_mit_Namen()
    {
        var t = Neu(Mode.Knockout, "A", "B", "C", "D");
        t.Draw(new Random(1));

        Assert.Contains("Sieger aus Halbfinale 1", t.Describe(t.Matches[2]));
        Assert.StartsWith("Halbfinale 1 (", t.Describe(t.Matches[0]));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 3)]
    [InlineData(8, 7)]
    [InlineData(9, 15)]
    [InlineData(16, 15)]
    public void Der_Baum_hat_immer_Groesse_minus_eins_Matches(int players, int matches)
    {
        var t = Neu(Mode.Knockout, Enumerable.Range(1, players).Select(i => $"S{i}").ToArray());
        t.Draw(new Random(1));

        Assert.Equal(matches, t.Matches.Count);
        Assert.Equal(KnockoutDraw.NextPowerOfTwo(players) - players, t.Matches.Count(m => m.IsBye));
        Assert.Equal("Finale", t.Matches[^1].Label);
    }

    [Fact]
    public void Ein_Freilos_steht_immer_auf_der_zweiten_Seite()
    {
        // Darauf verlässt sich die Domäne: wer ein Freilos hat, steht auf Seite 1
        // und kommt weiter. Der Setzbaum füllt die geraden Positionen zuerst, und
        // die sind nie überzählig — ändert sich das, muss dieser Test scheitern.
        foreach (var spieler in new[] { 3, 5, 6, 7, 9, 11, 31 })
        {
            var t = Neu(Mode.Knockout, [.. Enumerable.Range(1, spieler).Select(i => $"S{i}")]);
            t.Draw(new Random(spieler));

            foreach (var freilos in t.Matches.Where(m => m.IsBye))
            {
                Assert.Equal(SideKind.Participant, freilos.Side1.Kind);
                Assert.Equal(SideKind.Bye, freilos.Side2.Kind);
                Assert.Equal(1, freilos.Score!.WinnerSide);
            }
        }
    }

    [Fact]
    public void Ein_grosser_Baum_zaehlt_seine_Runden_durch()
    {
        var t = Neu(Mode.Knockout, [.. Enumerable.Range(1, 32).Select(i => $"S{i}")]);
        t.Draw(new Random(7));

        var namen = t.Matches.Select(m => m.Label).ToList();

        Assert.Contains("Runde 1 1", namen);
        Assert.Contains("Achtelfinale 1", namen);
        Assert.Contains("Viertelfinale 1", namen);
        Assert.Contains("Halbfinale 1", namen);
        Assert.Contains("Finale", namen);
    }

    [Fact]
    public void Mehr_als_vierundsechzig_Teilnehmer_passen_nicht()
    {
        var t = Tournament.Create("Massenandrang", "browser-1", Now);

        for (var i = 1; i <= Tournament.MaxParticipants; i++)
        {
            t.AddParticipant($"S{i}");
        }

        Assert.Throws<DomainException>(() => t.AddParticipant("Einer zu viel"));
    }

    [Fact]
    public void Ein_zu_langer_Name_wird_abgewiesen()
    {
        var t = Neu(Mode.Knockout);

        Assert.Throws<DomainException>(() => t.AddParticipant(new string('x', 81)));
        t.AddParticipant(new string('x', 80));
    }

    [Fact]
    public void Vor_der_Auslosung_gibt_es_keine_Ergebnisse()
    {
        var t = Neu(Mode.Knockout, "Rudi", "Max");

        Assert.Throws<DomainException>(() => t.RecordResult(Guid.NewGuid(), Score.Walkover(absentSide: 2)));
        Assert.Throws<DomainException>(() => t.ClearResult(Guid.NewGuid()));
    }

    [Fact]
    public void Ein_Match_das_es_nicht_gibt()
    {
        var t = Neu(Mode.Knockout, "Rudi", "Max");
        t.Draw(new Random(1));

        Assert.Throws<DomainException>(() => t.FindMatch(Guid.NewGuid()));
    }

    [Fact]
    public void Beide_Halbfinals_lassen_sich_einzeln_zuruecknehmen()
    {
        var t = Neu(Mode.Knockout, "Rudi", "Max", "Anna", "Tom");
        t.Draw(new Random(1));

        var halbfinals = t.Matches.Where(m => m.Round == 1).ToList();
        var finale = t.Matches.Single(m => m.Round == 2);

        t.RecordResult(halbfinals[0].Id, Sieg1(t));
        t.RecordResult(halbfinals[1].Id, Sieg1(t));
        Assert.Equal(MatchStatus.Ready, finale.Status);

        // Das zweite zurücknehmen macht die zweite Seite des Finales wieder offen,
        // die erste bleibt besetzt.
        t.ClearResult(halbfinals[1].Id);
        Assert.Equal(SideKind.Participant, finale.Side1.Kind);
        Assert.Equal(SideKind.WinnerOf, finale.Side2.Kind);

        t.ClearResult(halbfinals[0].Id);
        Assert.Equal(SideKind.WinnerOf, finale.Side1.Kind);
        Assert.Equal(MatchStatus.Pending, finale.Status);
    }

    [Fact]
    public void Ein_Name_der_nicht_mehr_da_ist_heisst_Fragezeichen()
    {
        // So etwas entsteht nur, wenn die Datenbank etwas Widersprüchliches hergibt.
        var verschwunden = Guid.NewGuid();
        var schnappschuss = new TournamentSnapshot(
            Guid.NewGuid(),
            "Cup",
            null,
            null,
            Mode.Knockout,
            MatchFormat.Standard,
            TournamentState.Running,
            "browser-1",
            "token-lang-genug-fuer-den-test",
            Now,
            [],
            [new MatchSnapshot(Guid.NewGuid(), 1, 1, "Finale", Side.Of(verschwunden), Side.Bye, null)]);

        var t = Tournament.FromSnapshot(schnappschuss);

        Assert.Equal("Finale (? – Freilos)", t.Describe(t.Matches[0]));
    }

    private static Match Find(Tournament t, string a, string b) =>
        t.Matches.Single(m =>
            (t.NameOf(m.Side1) == a && t.NameOf(m.Side2) == b) || (t.NameOf(m.Side1) == b && t.NameOf(m.Side2) == a));

    private static void Gewinnt(Tournament t, Match m, string winner, SetScore[] sets)
    {
        var flip = t.NameOf(m.Side1) != winner;
        var oriented = sets.Select(s => flip ? new SetScore(s.Games2, s.Games1, s.TiebreakPoints) : s).ToList();
        t.RecordResult(m.Id, Score.Played(oriented, t.Format));
    }
}
