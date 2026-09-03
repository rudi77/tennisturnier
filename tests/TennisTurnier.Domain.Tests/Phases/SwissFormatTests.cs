using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;

namespace TennisTurnier.Domain.Tests.Phases;

/// <summary>
/// Das Schweizer System ist das einzige Format, das im Verlauf paart. Getestet
/// wird deshalb nicht eine einzelne Paarung, sondern der Durchlauf: Runde für
/// Runde ansetzen, Ergebnisse eintragen, wieder ansetzen — und dabei prüfen,
/// dass keine Paarung ein zweites Mal entsteht.
/// </summary>
public sealed class SwissFormatTests
{
    private static readonly MatchFormat Standard = new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6);

    private static PhaseDefinition Definition(int? rounds = null) => new()
    {
        Ordinal = 1,
        Format = PhaseFormatKind.Swiss,
        Rounds = rounds,
        Scoring = new ScoringRules(Win: 1, Loss: 0, Walkover: 0),
        Tiebreakers = [Tiebreaker.Buchholz, Tiebreaker.SetRatio, Tiebreaker.GameRatio, Tiebreaker.Lot],
    };

    private static IReadOnlyList<SeededEntry> Entries(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new SeededEntry(Guid.NewGuid(), i, $"Spielerin {i:00}"))];

    /// <summary>
    /// Ein Turnier, das sich Runde für Runde führen lässt — dieselbe Abfolge, die
    /// der <c>PhaseOrchestrator</c> in der Anwendungsschicht fährt.
    /// </summary>
    private sealed class Turnier
    {
        private readonly SwissFormat _format = new();
        private readonly PhaseDefinition _definition;

        internal Turnier(int players, int? rounds = null)
        {
            _definition = Definition(rounds);
            Entries = SwissFormatTests.Entries(players);
            Phase = new Phase(Guid.NewGuid(), Guid.NewGuid(), 1, PhaseFormatKind.Swiss, "Schweizer System");
        }

        internal Phase Phase { get; }

        internal IReadOnlyList<SeededEntry> Entries { get; }

        internal PhaseState State => new(_definition, Entries, Phase.Matches);

        internal bool IsComplete => _format.IsComplete(State);

        internal Standings Standings => _format.ComputeStandings(State);

        /// <summary>Setzt an, was anzusetzen ist. Gibt die Zahl der neuen Matches zurück.</summary>
        internal int Advance(IReadOnlySet<Guid>? onCourt = null)
        {
            Phase.Withdraw(_format.ObsoletePairings(State), onCourt ?? new HashSet<Guid>());

            var pairings = _format.GeneratePairings(State);
            Phase.AddPairings(pairings);

            return pairings.Count;
        }

        internal IReadOnlyList<Match> Round(int round) =>
            [.. Phase.Matches.Where(m => m.Round == round).OrderBy(m => m.Position)];

        /// <summary>
        /// Spielt eine Runde aus. <paramref name="upsetEvery"/> lässt jedes n-te
        /// Match anders ausgehen, als die Setzung erwarten ließe — sonst gewinnt
        /// immer der Bessere und die Punktgruppen bleiben trivial.
        /// </summary>
        internal void PlayRound(int round, int upsetEvery = 0)
        {
            var index = 0;

            foreach (var match in Round(round).Where(m => m.Status != MatchStatus.Finished))
            {
                var side = upsetEvery > 0 && ++index % upsetEvery == 0 ? 2 : 1;
                Win(match, side);
            }
        }

        internal void Win(Match match, int side) =>
            Phase.RecordResult(
                match.Id,
                Score.Played(
                    side == 1
                        ? [new SetScore(6, 4), new SetScore(6, 2)]
                        : [new SetScore(4, 6), new SetScore(2, 6)],
                    Standard));
    }

    private static IReadOnlyList<(Guid, Guid)> PairsOf(IEnumerable<Match> matches) =>
    [
        .. matches
            .Where(m => m.Side1.EntryId is not null && m.Side2.EntryId is not null)
            .Select(m => Ordered(m.Side1.EntryId!.Value, m.Side2.EntryId!.Value)),
    ];

    private static (Guid, Guid) Ordered(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);

    // --- Rundenzahl --------------------------------------------------------

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 3)]
    [InlineData(16, 4)]
    [InlineData(32, 5)]
    [InlineData(20, 5)]
    public void Ohne_Angabe_werden_so_viele_Runden_gespielt_wie_ein_KO_Baum_haette(int players, int expected)
    {
        var turnier = new Turnier(players);

        var rounds = 0;
        while (turnier.Advance() > 0)
        {
            turnier.PlayRound(++rounds, upsetEvery: 3);
        }

        Assert.Equal(expected, rounds);
        Assert.True(turnier.IsComplete);
    }

    [Fact]
    public void Eine_Vorgabe_geht_der_Voreinstellung_vor()
    {
        var turnier = new Turnier(32, rounds: 7);

        var rounds = 0;
        while (turnier.Advance() > 0)
        {
            turnier.PlayRound(++rounds, upsetEvery: 3);
        }

        Assert.Equal(7, rounds);
    }

    [Fact]
    public void Mehr_Runden_als_moegliche_Gegner_werden_beim_Auslosen_abgewiesen()
    {
        var turnier = new Turnier(8, rounds: 8);

        var error = Assert.Throws<DomainException>(() => turnier.Advance());

        Assert.Contains("möglich sind 7", error.Message, StringComparison.Ordinal);
    }

    // --- Die eigentliche Zusage: keine Wiederholungen -----------------------

    [Fact]
    public void Sieben_Runden_mit_32_Teilnehmern_ohne_eine_einzige_Wiederholung()
    {
        var turnier = new Turnier(32, rounds: 7);
        var seen = new HashSet<(Guid, Guid)>();

        for (var round = 1; round <= 7; round++)
        {
            Assert.Equal(16, turnier.Advance());

            foreach (var pair in PairsOf(turnier.Round(round)))
            {
                Assert.True(seen.Add(pair), $"Runde {round} wiederholt eine Paarung.");
            }

            turnier.PlayRound(round, upsetEvery: 3);
        }

        Assert.Equal(7 * 16, seen.Count);
        Assert.True(turnier.IsComplete);
    }

    /// <summary>
    /// Spielt ein Turnier mit zufälligen Ergebnissen durch und gibt zurück, was
    /// dabei passiert ist: die gesehenen Paarungen, die Freilose und die Zahl der
    /// Wiederholungen.
    /// </summary>
    private static (List<(Guid, Guid)> Pairs, List<Guid> Byes, int Rematches) Simulate(
        int players,
        int rounds,
        int seed)
    {
        var random = new Random(seed);
        var turnier = new Turnier(players, rounds);
        var pairs = new List<(Guid, Guid)>();
        var byes = new List<Guid>();
        var rematches = 0;

        for (var round = 1; round <= rounds; round++)
        {
            Assert.True(turnier.Advance() > 0, $"Runde {round} wurde nicht angesetzt.");

            foreach (var match in turnier.Round(round))
            {
                if (match.HasBye)
                {
                    byes.Add((match.Side1.IsBye ? match.Side2 : match.Side1).EntryId!.Value);
                    continue;
                }

                pairs.Add(Ordered(match.Side1.EntryId!.Value, match.Side2.EntryId!.Value));

                if (match.Label?.Contains("Wiederholung", StringComparison.Ordinal) == true)
                {
                    rematches++;
                }

                turnier.Win(match, random.Next(2) + 1);
            }
        }

        return (pairs, byes, rematches);
    }

    /// <summary>
    /// Über den Ergebnisverlauf hinweg darf keine Paarung zweimal entstehen und
    /// niemand zweimal aussetzen.
    ///
    /// Der Verlauf ist der einzige Freiheitsgrad des Verfahrens — an ihm hängt die
    /// Tabelle, an der Tabelle die Punktgruppen und daran die Paarung. Deshalb
    /// wird über <em>viele</em> Verläufe geprüft und nicht über einen: mit einem
    /// einzigen Startwert prüft man, ob dieser eine gut geht, und das ist keine
    /// Eigenschaft, sondern ein Glücksfall.
    /// </summary>
    [Theory]
    [InlineData(16, 4)]
    [InlineData(24, 5)]
    [InlineData(11, 5)]
    [InlineData(13, 4)]
    [InlineData(9, 4)]
    [InlineData(30, 5)]
    [InlineData(7, 3)]
    public void Bei_vorgesehener_Rundenzahl_erzwingt_kein_Verlauf_eine_Wiederholung(int players, int rounds)
    {
        for (var seed = 1; seed <= 40; seed++)
        {
            var (pairs, byes, rematches) = Simulate(players, rounds, seed);

            Assert.Equal(rounds * (players / 2), pairs.Count);
            Assert.Equal(pairs.Count, pairs.Distinct().Count());
            Assert.Equal(0, rematches);

            // Jeder spielt jede Runde, abzüglich des einen Freiloses bei
            // ungerader Teilnehmerzahl.
            Assert.Equal(players % 2 == 0 ? 0 : rounds, byes.Count);
            Assert.Equal(byes.Count, byes.Distinct().Count());
        }
    }

    /// <summary>
    /// Bis zur Grenze des Möglichen kann das Verfahren sich festfahren — es paart
    /// jede Runde nach dem Stand von jetzt und schaut nicht voraus. Dann gibt es
    /// eine ausgewiesene Wiederholung, und das Turnier läuft weiter.
    ///
    /// Der Punkt ist nicht die Wiederholung, sondern dass nichts abbricht: eine
    /// Ausnahme hier hieße, dass sich das letzte Ergebnis der vorigen Runde nicht
    /// mehr eintragen lässt und das Turnier ohne Vor- und Rückweg steht.
    /// </summary>
    [Theory]
    [InlineData(8, 7)]
    [InlineData(12, 11)]
    [InlineData(16, 15)]
    [InlineData(9, 9)]
    [InlineData(11, 11)]
    public void An_der_Grenze_wiederholt_das_Verfahren_lieber_als_stehenzubleiben(int players, int rounds)
    {
        var withRematch = 0;

        for (var seed = 1; seed <= 25; seed++)
        {
            var (pairs, byes, rematches) = Simulate(players, rounds, seed);

            // Vollständig bleibt die Runde in jedem Fall: jeder hat einen Gegner.
            Assert.Equal(rounds * (players / 2), pairs.Count);
            Assert.Equal(players % 2 == 0 ? 0 : rounds, byes.Count);
            Assert.Equal(byes.Count, byes.Distinct().Count());

            // Und jede Wiederholung ist als solche beschriftet.
            Assert.Equal(pairs.Count - pairs.Distinct().Count(), rematches);

            if (rematches > 0)
            {
                withRematch++;
            }
        }

        // Nicht als Zusage, sondern als Beleg dafür, dass dieser Test den Fall
        // wirklich trifft: an der Grenze kommt er vor.
        Assert.True(withRematch > 0, "Kein Verlauf erreichte die Grenze — der Test prüft nichts.");
    }

    // --- Paarung nach Punktestand ------------------------------------------

    [Fact]
    public void Die_erste_Runde_paart_die_obere_gegen_die_untere_Haelfte()
    {
        var turnier = new Turnier(8);
        turnier.Advance();

        var expected = Enumerable.Range(0, 4)
            .Select(i => Ordered(turnier.Entries[i].EntryId, turnier.Entries[i + 4].EntryId))
            .ToList();

        Assert.Equal(expected, PairsOf(turnier.Round(1)));
    }

    [Fact]
    public void Ab_der_zweiten_Runde_treffen_Punktgleiche_aufeinander()
    {
        var turnier = new Turnier(8);
        turnier.Advance();
        turnier.PlayRound(1);

        turnier.Advance();

        var points = turnier.Standings.Places.ToDictionary(p => p.EntryId, p => p.Points);

        foreach (var match in turnier.Round(2))
        {
            Assert.Equal(points[match.Side1.EntryId!.Value], points[match.Side2.EntryId!.Value]);
        }
    }

    /// <summary>
    /// Bei ungerader Punktgruppe steigt der Letzte ab und trifft auf die
    /// nächstschwächere — nicht der Erste auf die nächststärkere. Andersherum
    /// bekäme wer in seiner Gruppe hinten steht die leichtere Aufgabe.
    /// </summary>
    [Fact]
    public void Aus_einer_ungeraden_Punktgruppe_steigt_der_Letzte_ab()
    {
        var turnier = new Turnier(6);
        turnier.Advance();

        // Zwei Sieger, ein Verlierer je Punktgruppe: 1 und 2 gewinnen, 6 gewinnt
        // gegen 3. Damit stehen drei auf einem Punkt und drei auf null.
        var round1 = turnier.Round(1);
        turnier.Win(round1[0], 1);
        turnier.Win(round1[1], 1);
        turnier.Win(round1[2], 2);

        turnier.Advance();

        var points = turnier.Standings.Places.ToDictionary(p => p.EntryId, p => p.Points);
        var mixed = turnier.Round(2)
            .Where(m => points[m.Side1.EntryId!.Value] != points[m.Side2.EntryId!.Value])
            .ToList();

        var floater = Assert.Single(mixed);
        var order = turnier.Standings.Places.Select(p => p.EntryId).ToList();

        // Der Absteiger ist der Letzte seiner Punktgruppe, nicht der Erste.
        var descending = order.IndexOf(floater.Side1.EntryId!.Value) < order.IndexOf(floater.Side2.EntryId!.Value)
            ? floater.Side1.EntryId!.Value
            : floater.Side2.EntryId!.Value;

        var winners = order.Where(id => points[id] == 1).ToList();
        Assert.Equal(winners[^1], descending);
    }

    // --- Freilos -----------------------------------------------------------

    [Fact]
    public void Bei_ungerader_Teilnehmerzahl_setzt_jede_Runde_einer_aus()
    {
        var turnier = new Turnier(7, rounds: 3);

        for (var round = 1; round <= 3; round++)
        {
            turnier.Advance();
            Assert.Single(turnier.Round(round), m => m.HasBye);
            turnier.PlayRound(round, upsetEvery: 2);
        }
    }

    [Fact]
    public void Ein_Freilos_zaehlt_wie_ein_Sieg()
    {
        var turnier = new Turnier(5, rounds: 2);
        turnier.Advance();

        var bye = Assert.Single(turnier.Round(1), m => m.HasBye);
        var resting = (bye.Side1.IsBye ? bye.Side2 : bye.Side1).EntryId!.Value;

        turnier.PlayRound(1);

        var standing = turnier.Standings.Places.Single(p => p.EntryId == resting);

        Assert.Equal(1, standing.Won);
        Assert.Equal(1, standing.Points);
    }

    /// <summary>
    /// Ein zweites Freilos wäre ein zweiter geschenkter Punkt — und der
    /// entscheidet ein Turnier, in dem alle anderen ihn erspielen müssen.
    /// </summary>
    [Fact]
    public void Niemand_bekommt_zweimal_ein_Freilos()
    {
        var turnier = new Turnier(9, rounds: 4);
        var resting = new List<Guid>();

        for (var round = 1; round <= 4; round++)
        {
            turnier.Advance();

            var bye = Assert.Single(turnier.Round(round), m => m.HasBye);
            resting.Add((bye.Side1.IsBye ? bye.Side2 : bye.Side1).EntryId!.Value);

            turnier.PlayRound(round, upsetEvery: 2);
        }

        Assert.Equal(4, resting.Distinct().Count());
    }

    // --- Tabelle -----------------------------------------------------------

    /// <summary>
    /// Buchholz trennt Punktgleiche danach, gegen wen sie spielen mussten. Ohne
    /// dieses Kriterium wäre eine Tabelle des Schweizer Systems weitgehend
    /// aussagelos: nach fünf Runden stehen regelmäßig ein halbes Dutzend Spieler
    /// auf demselben Punktestand.
    /// </summary>
    [Fact]
    public void Buchholz_trennt_Punktgleiche_nach_der_Staerke_ihrer_Gegner()
    {
        var turnier = new Turnier(8, rounds: 3);

        for (var round = 1; round <= 3; round++)
        {
            turnier.Advance();
            turnier.PlayRound(round);
        }

        var places = turnier.Standings.Places;

        // Wer die stärkeren Gegner hatte, steht bei gleichem Punktestand vorn.
        // Der Erwartungswert wird hier aus den Matches gerechnet und nicht der
        // Implementierung abgeschaut: Summe der Punkte aller Gegner.
        var points = places.ToDictionary(p => p.EntryId, p => p.Points);

        int Buchholz(Guid entryId) => turnier.Phase.Matches
            .Where(m => !m.HasBye && (m.Side1.EntryId == entryId || m.Side2.EntryId == entryId))
            .Select(m => m.Side1.EntryId == entryId ? m.Side2.EntryId!.Value : m.Side1.EntryId!.Value)
            .Sum(opponent => points.GetValueOrDefault(opponent));

        var tied = places.GroupBy(p => p.Points).Where(g => g.Count() > 1).ToList();
        Assert.NotEmpty(tied);

        foreach (var group in tied)
        {
            var buchholz = group.OrderBy(p => p.Rank).Select(p => Buchholz(p.EntryId)).ToList();

            Assert.Equal(buchholz.OrderByDescending(v => v), buchholz);
        }
    }

    /// <summary>
    /// Buchholz zählt die <em>Punkte</em> der Gegner, nicht ihre Siege.
    ///
    /// Der Unterschied wird sichtbar, sobald ein Sieg nicht die volle Punktzahl
    /// bringt. Hier gewinnt Spielerin 3 kampflos: ein Sieg, aber null Punkte. Wer
    /// gegen sie gespielt hat, hat damit einen schwachen Gegner gehabt — nach
    /// Siegen gerechnet einen starken, und die Tabelle stünde anders herum.
    /// </summary>
    [Fact]
    public void Buchholz_zaehlt_die_Punkte_der_Gegner_und_nicht_ihre_Siege()
    {
        var definition = new PhaseDefinition
        {
            Ordinal = 1,
            Format = PhaseFormatKind.Swiss,
            Rounds = 2,
            Scoring = new ScoringRules(Win: 2, Loss: 0, Walkover: 0),
            Tiebreakers = [Tiebreaker.Buchholz, Tiebreaker.Lot],
        };

        var entries = Entries(4);
        var phase = new Phase(Guid.NewGuid(), Guid.NewGuid(), 1, PhaseFormatKind.Swiss, "Schweizer System");
        var format = new SwissFormat();
        var state = new PhaseState(definition, entries, phase.Matches);

        phase.AddPairings(format.GeneratePairings(state));

        // Runde 1 paart nach Setzung obere gegen untere Hälfte: 1–3 und 2–4.
        var first = phase.Matches.Single(m => m.Side1.EntryId == entries[0].EntryId);
        var second = phase.Matches.Single(m => m.Side1.EntryId == entries[1].EntryId);

        // Spielerin 1 tritt nicht an — Spielerin 3 gewinnt kampflos, ohne Punkte.
        phase.RecordResult(first.Id, Score.Walkover(absentSide: 1));
        phase.RecordResult(
            second.Id, Score.Played([new SetScore(6, 4), new SetScore(6, 2)], Standard));

        var places = format.ComputeStandings(state).Places;
        var rank = places.ToDictionary(p => p.EntryId, p => p.Rank);

        // Alle drei Verliererinnen stehen bei null Punkten. Spielerin 4 hatte mit
        // Spielerin 2 die einzige Gegnerin mit Punkten und steht deshalb vor
        // Spielerin 1 — nach Siegen gerechnet stünden beide gleich, und die
        // Setzung entschiede zugunsten von Spielerin 1.
        Assert.Equal(0, places.Single(p => p.EntryId == entries[3].EntryId).Points);
        Assert.Equal(0, places.Single(p => p.EntryId == entries[0].EntryId).Points);
        Assert.True(
            rank[entries[3].EntryId] < rank[entries[0].EntryId],
            "Spielerin 4 hatte die punktstärkere Gegnerin und muss vor Spielerin 1 stehen.");
    }

    [Fact]
    public void Vor_der_letzten_Runde_ist_die_Phase_nicht_durch()
    {
        var turnier = new Turnier(8, rounds: 4);

        turnier.Advance();
        turnier.PlayRound(1);

        // Alle angelegten Matches sind entschieden — die Phase ist es nicht.
        Assert.All(turnier.Phase.Matches, m => Assert.Equal(MatchStatus.Finished, m.Status));
        Assert.False(turnier.IsComplete);
    }

    // --- Korrektur ---------------------------------------------------------

    /// <summary>
    /// Wird ein Ergebnis zurückgenommen, stammt die Paarung der Folgerunden aus
    /// einer Tabelle, die es nicht mehr gibt. Sie stehen zu lassen hieße, mit
    /// Paarungen weiterzuspielen, die niemand mehr herleiten kann.
    /// </summary>
    [Fact]
    public void Eine_Korrektur_nimmt_die_daraus_entstandenen_Runden_zurueck()
    {
        var turnier = new Turnier(8, rounds: 3);
        turnier.Advance();
        turnier.PlayRound(1);
        turnier.Advance();

        Assert.Equal(4, turnier.Round(2).Count);

        var corrected = turnier.Round(1)[0];
        turnier.Phase.ClearResult(corrected.Id);
        turnier.Advance();

        Assert.Empty(turnier.Round(2));

        // Und mit dem anderen Ausgang entsteht eine andere zweite Runde.
        turnier.Win(corrected, 2);
        turnier.Advance();

        Assert.Equal(4, turnier.Round(2).Count);
    }

    /// <summary>
    /// Bei ungerader Teilnehmerzahl enthält jede Runde ein Freilos, und das
    /// Freilos trägt einen Spielstand. Zählte es als gespielt, wäre die
    /// Folgerunde nicht mehr zurückzunehmen — und ihr Freilos-Ergebnis auch
    /// nicht, denn ein Freilos hat keines, das sich zurücknehmen ließe.
    ///
    /// Das war eine Sackgasse ohne Ausgang: ab der zweiten Runde ließ sich kein
    /// Ergebnis einer früheren mehr korrigieren. Mit acht Spielern, wie im Test
    /// darüber, kommt sie nicht vor.
    /// </summary>
    [Fact]
    public void Eine_Korrektur_gelingt_auch_wenn_die_Folgerunde_ein_Freilos_enthaelt()
    {
        var turnier = new Turnier(5, rounds: 3);
        turnier.Advance();
        turnier.PlayRound(1);
        turnier.Advance();

        // Fünf Spieler: zwei Paarungen und ein Freilos, in jeder Runde.
        Assert.Equal(3, turnier.Round(2).Count);
        Assert.Contains(turnier.Round(2), m => m.HasBye);

        var corrected = turnier.Round(1).First(m => !m.HasBye);
        turnier.Phase.ClearResult(corrected.Id);
        turnier.Advance();

        Assert.Empty(turnier.Round(2));

        // Und mit dem anderen Ausgang entsteht eine andere zweite Runde.
        turnier.Win(corrected, 2);
        turnier.Advance();

        Assert.Equal(3, turnier.Round(2).Count);
    }

    /// <summary>
    /// Ein Match, das schon am Platz steht, wird nicht zurückgenommen. Es
    /// verschwände samt seiner Platzzuweisung, während zwei Spielerinnen darauf
    /// spielen — und in der öffentlichen Ansicht wäre die Partie nie gewesen.
    /// </summary>
    [Fact]
    public void Eine_Paarung_am_Platz_wird_nicht_zurueckgenommen()
    {
        var turnier = new Turnier(8, rounds: 3);
        turnier.Advance();
        turnier.PlayRound(1);
        turnier.Advance();

        var onCourt = turnier.Round(2)[0];
        turnier.Phase.ClearResult(turnier.Round(1)[0].Id);

        var error = Assert.Throws<DomainException>(
            () => turnier.Advance(new HashSet<Guid> { onCourt.Id }));

        Assert.Contains("steht bereits am Platz", error.Message, StringComparison.Ordinal);
        Assert.Equal(8, turnier.Phase.Matches.Count);
    }

    [Fact]
    public void Eine_bereits_gespielte_Folgerunde_verhindert_die_Korrektur()
    {
        var turnier = new Turnier(8, rounds: 3);
        turnier.Advance();
        turnier.PlayRound(1);
        turnier.Advance();
        turnier.PlayRound(2);

        turnier.Phase.ClearResult(turnier.Round(1)[0].Id);

        var error = Assert.Throws<DomainException>(() => turnier.Advance());

        Assert.Contains("bereits gespielt", error.Message, StringComparison.Ordinal);
    }

    // --- Definition --------------------------------------------------------

    [Fact]
    public void Das_Schweizer_System_ist_keine_Endrunde()
    {
        var definition = new FormatDefinition
        {
            Id = "gruppe-dann-schweiz",
            Name = "Unsinn",
            Phases =
            [
                new PhaseDefinition { Ordinal = 1, Format = PhaseFormatKind.RoundRobin },
                new PhaseDefinition
                {
                    Ordinal = 2,
                    Format = PhaseFormatKind.Swiss,
                    Qualification = new Qualification(1, QualificationRule.All),
                },
            ],
        };

        var error = Assert.Throws<DomainException>(definition.Validate);

        Assert.Contains("nur als erste Phase", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Die_mitgelieferte_Vorlage_ist_gueltig()
    {
        BuiltInFormats.Swiss.Validate();

        Assert.Equal(PhaseFormatKind.Swiss, BuiltInFormats.Swiss.Phases[0].Format);
        Assert.Contains(Tiebreaker.Buchholz, BuiltInFormats.Swiss.Phases[0].Tiebreakers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Ein_Schweizer_System_braucht_einen_Gegner(int teilnehmer)
    {
        var phase = new Phase(Guid.NewGuid(), Guid.NewGuid(), 1, PhaseFormatKind.Swiss);
        var state = new PhaseState(Definition(), Entries(teilnehmer), phase.Matches);

        var fehler = Assert.Throws<DomainException>(() => new SwissFormat().GeneratePairings(state));

        Assert.Contains("mindestens zwei Teilnehmer", fehler.Message, StringComparison.Ordinal);
        Assert.Contains($"hatte {teilnehmer}", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_Freilos_auf_der_ersten_Seite_zaehlt_genauso()
    {
        // Das Format setzt das Freilos selbst immer auf die zweite Seite. Die
        // Matches kommen aber aus der Ablage, und eine Phase, die aus einer
        // anderen Fassung stammt, kann es andersherum stehen haben. Zählte es
        // dann nicht, bekäme derselbe Spieler ein zweites Freilos — einen
        // zweiten geschenkten Punkt.
        var format = new SwissFormat();
        var entries = Entries(3);
        var phase = new Phase(Guid.NewGuid(), Guid.NewGuid(), 1, PhaseFormatKind.Swiss);

        var matches = phase.AddPairings([
            new Pairing(1, 1, ParticipantRef.ByeSlot, ParticipantRef.Of(entries[2].EntryId), "Runde 1"),
            new Pairing(
                1, 2,
                ParticipantRef.Of(entries[0].EntryId),
                ParticipantRef.Of(entries[1].EntryId),
                "Runde 1"),
        ]);

        phase.RecordResult(
            matches[1].Id,
            Score.Played([new SetScore(6, 4), new SetScore(6, 2)], Standard));

        var zweite = format.GeneratePairings(
            new PhaseState(Definition(rounds: 2), entries, phase.Matches));

        var freilos = zweite.SingleOrDefault(p => p.Side1 is ParticipantRef.Bye || p.Side2 is ParticipantRef.Bye);

        Assert.NotNull(freilos);
        Assert.NotEqual(entries[2].EntryId, freilos.Side1.EntryIdOrDefault());
    }

}

internal static class ParticipantRefTestExtensions
{
    /// <summary>Die Meldung hinter einer Seite, sofern sie eine hat.</summary>
    internal static Guid EntryIdOrDefault(this ParticipantRef origin) =>
        origin is ParticipantRef.Entry entry ? entry.EntryId : Guid.Empty;
}
