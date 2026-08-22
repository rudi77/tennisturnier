using TennisTurnier.Adapters.Scheduling;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Adapters.Scheduling.Tests;

/// <summary>
/// Der Spielplan-Solver, geprüft am eigenen Prüfer: was er vorschlägt, muss der
/// <see cref="ScheduleValidator"/> ohne Verstoß durchgehen lassen. Ein Solver,
/// der seine Ergebnisse selbst für zulässig erklärt, prüft nichts (ADR-0002).
/// </summary>
public sealed class HeuristicScheduleSolverTests
{
    private static readonly MatchFormat Standard = new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6);
    private static readonly TimeSpan Rest = TimeSpan.FromMinutes(30);

    private readonly HeuristicScheduleSolver _solver = new();
    private readonly Guid _tournamentId = Guid.NewGuid();

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 5, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>Zwei Turniertage von 8 bis 22 Uhr.</summary>
    private static IReadOnlyList<TimeSlot> TwoDays => [new(At(16, 8), At(16, 22)), new(At(17, 8), At(17, 22))];

    private static IReadOnlyList<SchedulableCourt> Courts(int count, int centerCourtIndex = -1) =>
        [.. Enumerable.Range(1, count).Select(i => new SchedulableCourt(
            Guid.NewGuid(), $"Platz {i}", i - 1 == centerCourtIndex, TwoDays))];

    private (Phase Phase, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> Players) Knockout(int participants)
    {
        var phase = new Phase(Guid.NewGuid(), _tournamentId, 1, PhaseFormatKind.Knockout);

        var entries = Enumerable.Range(1, participants)
            .Select(i => new SeededEntry(Guid.NewGuid(), i, $"Spielerin {i:00}"))
            .ToList();

        var state = new PhaseState(
            new PhaseDefinition { Ordinal = 1, Format = PhaseFormatKind.Knockout },
            entries,
            phase.Matches);

        phase.AddPairings(new KnockoutFormat().GeneratePairings(state));

        var players = entries.ToDictionary(
            entry => entry.EntryId,
            entry => (IReadOnlyList<Guid>)[Guid.NewGuid()]);

        return (phase, players);
    }

    private static SchedulingProblem Problem(
        Phase phase,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> players,
        IReadOnlyList<SchedulableCourt> courts,
        IReadOnlyList<CourtAssignment>? existing = null) =>
        new(
            phase.Matches,
            players,
            courts,
            phase.Matches.ToDictionary(match => match.Id, _ => TimeSpan.FromMinutes(75)),
            Rest,
            existing ?? []);

    private static IReadOnlyList<ScheduleViolation> Violations(
        ScheduleProposal proposal,
        SchedulingProblem problem)
    {
        var assignments = proposal.Assignments.Select(assignment =>
        {
            var candidate = new CourtAssignment(
                Guid.NewGuid(),
                Guid.NewGuid(),
                assignment.MatchId,
                assignment.CourtId,
                assignment.SequenceOnCourt,
                assignment.EstimatedDuration,
                AssignmentSource.Auto);

            candidate.PlanFor(assignment.PlannedStart);

            return candidate;
        }).ToList();

        return new ScheduleValidator().Validate(assignments, problem.ToContext());
    }

    [Fact]
    public void Ein_Turnier_mit_32_Teilnehmern_auf_vier_Plaetzen_bekommt_einen_gueltigen_Plan()
    {
        // Die Abnahmebedingung aus dem Fahrplan.
        var (phase, players) = Knockout(32);
        var problem = Problem(phase, players, Courts(4));

        var proposal = _solver.Solve(problem);

        Assert.Empty(proposal.Unscheduled);
        Assert.Equal(phase.Matches.Count, proposal.Assignments.Count);
        Assert.Empty(Violations(proposal, problem));
    }

    [Fact]
    public void Ein_Folgematch_beginnt_nach_dem_Ende_seiner_Vorgaenger()
    {
        var (phase, players) = Knockout(8);
        var problem = Problem(phase, players, Courts(2));

        var proposal = _solver.Solve(problem);
        var byMatch = proposal.Assignments.ToDictionary(assignment => assignment.MatchId);

        foreach (var match in phase.Matches)
        {
            foreach (var predecessorId in Predecessors(match))
            {
                Assert.True(
                    byMatch[match.Id].PlannedStart >= byMatch[predecessorId].PlannedEnd + Rest,
                    $"{byMatch[match.Id].PlannedStart:HH:mm} liegt nicht nach {byMatch[predecessorId].PlannedEnd:HH:mm}.");
            }
        }
    }

    private static IEnumerable<Guid> Predecessors(Match match)
    {
        if (match.Side1.Origin.DependsOnMatch is { } first)
        {
            yield return first;
        }

        if (match.Side2.Origin.DependsOnMatch is { } second)
        {
            yield return second;
        }
    }

    [Fact]
    public void Jede_Ansetzung_traegt_eine_Begruendung()
    {
        // Der Unterschied zwischen einem Werkzeug, das benutzt wird, und einem,
        // das man umgeht.
        var (phase, players) = Knockout(8);

        var proposal = _solver.Solve(Problem(phase, players, Courts(2)));

        Assert.All(proposal.Assignments, assignment => Assert.False(string.IsNullOrWhiteSpace(assignment.Reason)));
    }

    [Fact]
    public void Ohne_Platz_bleibt_jedes_Match_unangesetzt_und_sagt_warum()
    {
        var (phase, players) = Knockout(4);

        var proposal = _solver.Solve(Problem(phase, players, []));

        Assert.Empty(proposal.Assignments);
        Assert.Equal(phase.Matches.Count, proposal.Unscheduled.Count);
        Assert.All(proposal.Unscheduled, un => Assert.Contains("Platz", un.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public void Ein_zu_kurzes_Zeitfenster_laesst_Matches_uebrig()
    {
        // Lieber ein ehrliches „passt nicht" als ein Plan, der am Turniertag
        // zerfällt.
        var (phase, players) = Knockout(8);
        var court = new SchedulableCourt(Guid.NewGuid(), "Platz 1", false, [new TimeSlot(At(16, 9), At(16, 12))]);

        var proposal = _solver.Solve(Problem(phase, players, [court]));

        Assert.NotEmpty(proposal.Unscheduled);
        Assert.Empty(Violations(proposal, Problem(phase, players, [court])));
    }

    // --- Stabilität --------------------------------------------------------

    [Fact]
    public void Eine_von_Hand_gesetzte_Ansetzung_bleibt_unangetastet()
    {
        // Manuell und festgenagelt gehen als harte Vorgabe in den nächsten Lauf.
        // Die Turnierleitung kennt Umstände, die das System nicht kennt.
        var (phase, players) = Knockout(8);
        var courts = Courts(2);
        var match = phase.Matches.Last();

        var manual = new CourtAssignment(
            Guid.NewGuid(), _tournamentId, match.Id, courts[1].Id, 1,
            TimeSpan.FromMinutes(75), AssignmentSource.Manual);
        manual.PlanFor(At(17, 18));

        var proposal = _solver.Solve(Problem(phase, players, courts, [manual]));
        var kept = proposal.Assignments.Single(assignment => assignment.MatchId == match.Id);

        Assert.Equal(At(17, 18), kept.PlannedStart);
        Assert.Equal(courts[1].Id, kept.CourtId);
        Assert.Equal(ProposalChange.Unchanged, kept.Change);
        Assert.Contains("Hand", kept.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_erneuter_Lauf_ohne_Aenderung_ergibt_denselben_Plan()
    {
        // Ohne diese Eigenschaft wäre jeder Lauf ein Glücksspiel und der Aushang
        // nach jedem Klick ein anderer.
        var (phase, players) = Knockout(16);
        var courts = Courts(3);

        var first = _solver.Solve(Problem(phase, players, courts));
        var second = _solver.Solve(Problem(phase, players, courts));

        Assert.Equal(
            first.Assignments.Select(a => (a.MatchId, a.CourtId, a.PlannedStart)),
            second.Assignments.Select(a => (a.MatchId, a.CourtId, a.PlannedStart)));
    }

    [Fact]
    public void Nach_einer_Verschiebung_von_Hand_aendert_der_neue_Lauf_wenig()
    {
        // Der eigentliche Knackpunkt aus ADR-0002: ein Solver, der nach jeder
        // kleinen Änderung den halben Plan neu legt, wird abgeschaltet.
        //
        // Fünf Plätze für sechzehn Teilnehmer, also mit etwas Luft. In einem bis
        // zur letzten Minute vollen Plan bewegt eine Verschiebung um zwei Stunden
        // zwangsläufig die halbe Endrunde mit — das ist keine Unruhe des Solvers,
        // sondern die Folge der Verschiebung.
        var (phase, players) = Knockout(16);
        var courts = Courts(5);

        var first = _solver.Solve(Problem(phase, players, courts));

        // Der bestätigte Plan, dazu eine Verschiebung von Hand.
        var confirmed = first.Assignments.Select(assignment =>
        {
            var moved = assignment.MatchId == first.Assignments[0].MatchId;

            var stored = new CourtAssignment(
                Guid.NewGuid(),
                _tournamentId,
                assignment.MatchId,
                assignment.CourtId,
                assignment.SequenceOnCourt,
                assignment.EstimatedDuration,
                moved ? AssignmentSource.Manual : AssignmentSource.Auto);

            stored.PlanFor(moved ? assignment.PlannedStart.AddHours(2) : assignment.PlannedStart);

            return stored;
        }).ToList();

        var second = _solver.Solve(Problem(phase, players, courts, confirmed));

        // Die Verschobene bleibt.
        Assert.Equal(
            confirmed[0].PlannedStart,
            second.Assignments.Single(a => a.MatchId == confirmed[0].MatchId).PlannedStart);

        // Und wer sich sonst bewegt, hat einen Grund: entweder hängt er im Baum
        // an der verschobenen Partie, oder sein alter Termin liegt jetzt unter
        // ihr. Alles andere bliebe stehen — das ist die Behauptung, nicht eine
        // Zahl, die zufällig klein genug ist.
        var affected = Descendants(phase, confirmed[0].MatchId);
        var occupied = new TimeSlot(
            confirmed[0].PlannedStart!.Value,
            confirmed[0].PlannedStart!.Value + confirmed[0].EstimatedDuration);

        var before = first.Assignments.ToDictionary(a => a.MatchId);

        foreach (var moved in second.Assignments.Where(a => a.Change == ProposalChange.Moved))
        {
            var previous = before[moved.MatchId];
            var displaced = previous.CourtId == confirmed[0].CourtId
                && new TimeSlot(previous.PlannedStart, previous.PlannedEnd).Overlaps(occupied);

            Assert.True(
                affected.Contains(moved.MatchId) || displaced,
                $"„{previous.CourtName} {previous.PlannedStart:HH:mm}“ hat sich ohne Anlass bewegt.");
        }

        Assert.True(second.Diff.Unchanged > second.Diff.Moved);
    }

    /// <summary>Alle Matches, die im Baum hinter diesem hängen.</summary>
    private static HashSet<Guid> Descendants(Phase phase, Guid matchId)
    {
        var found = new HashSet<Guid>();
        var pending = new Queue<Guid>([matchId]);

        while (pending.TryDequeue(out var current))
        {
            foreach (var successor in phase.Matches.Where(m => Predecessors(m).Contains(current)))
            {
                if (found.Add(successor.Id))
                {
                    pending.Enqueue(successor.Id);
                }
            }
        }

        return found;
    }

    [Fact]
    public void Ein_gespieltes_Match_zwingt_seine_Nachfolger_nicht_zum_Umzug()
    {
        // Regression: „behalten" verlangte, dass jeder Vorgänger im selben Lauf
        // gelegt wurde. Ein gespieltes Match wird gar nicht mehr angesetzt, taucht
        // also nie auf — ab dem ersten Ergebnis konnte damit kein Nachfolger
        // seinen Termin behalten, und der Aushang änderte sich nach jedem
        // eingetragenen Ergebnis.
        var (phase, players) = Knockout(8);
        var courts = Courts(3);

        var first = _solver.Solve(Problem(phase, players, courts));

        var confirmed = first.Assignments.Select(assignment =>
        {
            var stored = new CourtAssignment(
                Guid.NewGuid(),
                _tournamentId,
                assignment.MatchId,
                assignment.CourtId,
                assignment.SequenceOnCourt,
                assignment.EstimatedDuration,
                AssignmentSource.Auto);

            stored.PlanFor(assignment.PlannedStart);

            return stored;
        }).ToList();

        // Das zuletzt endende Erstrundenmatch gilt als gespielt und fällt aus der
        // Aufgabe — genau wie nach einer Ergebniseingabe.
        var played = first.Assignments
            .Where(a => phase.Matches.Single(m => m.Id == a.MatchId).Round == 1)
            .MaxBy(a => a.PlannedEnd)!;

        var remaining = phase.Matches.Where(match => match.Id != played.MatchId).ToList();

        var problem = new SchedulingProblem(
            remaining,
            players,
            courts,
            remaining.ToDictionary(match => match.Id, _ => TimeSpan.FromMinutes(75)),
            Rest,
            confirmed);

        var second = _solver.Solve(problem);

        Assert.Equal(0, second.Diff.Moved);
        Assert.Equal(1, second.Diff.Removed);
    }

    [Fact]
    public void Der_Diff_zaehlt_was_sich_aendert()
    {
        var (phase, players) = Knockout(8);
        var courts = Courts(2);

        var fresh = _solver.Solve(Problem(phase, players, courts));

        Assert.Equal(phase.Matches.Count, fresh.Diff.Added);
        Assert.Equal(0, fresh.Diff.Moved);
        Assert.False(fresh.Diff.IsEmpty);
    }

    // --- Weiche Wünsche ----------------------------------------------------

    [Fact]
    public void Das_Finale_landet_auf_dem_Center_Court()
    {
        var (phase, players) = Knockout(8);
        var courts = Courts(3, centerCourtIndex: 1);
        var final = phase.Matches.Single(match => match.Round == phase.Matches.Max(m => m.Round));

        var proposal = _solver.Solve(Problem(phase, players, courts));

        Assert.Equal(
            courts[1].Id,
            proposal.Assignments.Single(assignment => assignment.MatchId == final.Id).CourtId);
    }

    [Fact]
    public void Kein_Spieler_steht_zweimal_gleichzeitig_auf_dem_Platz()
    {
        // Der Fall, den ein Plan ohne Spielerbezug reihenweise erzeugt: derselbe
        // Spieler in zwei Meldungen, etwa Einzel und Doppel.
        var (phase, players) = Knockout(8);

        var entryIds = players.Keys.ToList();
        var shared = Guid.NewGuid();
        var withDouble = players.ToDictionary(
            pair => pair.Key,
            pair => pair.Key == entryIds[0] || pair.Key == entryIds[4]
                ? (IReadOnlyList<Guid>)[shared]
                : pair.Value);

        var problem = Problem(phase, withDouble, Courts(4));

        Assert.Empty(Violations(_solver.Solve(problem), problem));
    }

    [Fact]
    public void Wer_in_zwei_Meldungen_steckt_spielt_nacheinander()
    {
        // Einzel und Doppel am selben Tag: zwei Meldungen, ein Mensch. Plätze
        // sind genug da — die Pause zwischen seinen Matches ist der einzige
        // Grund, das zweite später anzusetzen.
        var (phase, players) = Knockout(4);
        var erste = phase.Matches.Where(m => m.Round == 1).ToList();

        var doppelspieler = players[erste[0].Side1.EntryId!.Value][0];
        var geteilt = players.ToDictionary(
            paar => paar.Key,
            paar => paar.Key == erste[1].Side1.EntryId
                ? (IReadOnlyList<Guid>)[doppelspieler]
                : paar.Value);

        var problem = Problem(phase, geteilt, Courts(4));
        var proposal = _solver.Solve(problem);

        Assert.Empty(proposal.Unscheduled);
        Assert.Empty(Violations(proposal, problem));

        var eins = proposal.Assignments.Single(a => a.MatchId == erste[0].Id);
        var zwei = proposal.Assignments.Single(a => a.MatchId == erste[1].Id);

        // Zwischen den beiden liegt mindestens die Mindestpause.
        var frueher = eins.PlannedStart <= zwei.PlannedStart ? eins : zwei;
        var spaeter = frueher == eins ? zwei : eins;

        Assert.True(spaeter.PlannedStart >= frueher.PlannedStart + frueher.EstimatedDuration + Rest);
    }

    [Fact]
    public void Eine_bestehende_Ansetzung_ohne_Match_belegt_nur_den_Platz()
    {
        // Sie bleibt aus einem verworfenen Draw zurück. Der Platz gilt trotzdem
        // als belegt — dort steht jemand, auch wenn der Solver das Match nicht
        // mehr kennt.
        var (phase, players) = Knockout(4);
        var courts = Courts(1);

        var fremde = new CourtAssignment(
            Guid.NewGuid(),
            _tournamentId,
            Guid.NewGuid(),
            courts[0].Id,
            1,
            TimeSpan.FromMinutes(75),
            AssignmentSource.Manual);

        fremde.PlanFor(At(16, 8));

        var problem = Problem(phase, players, courts, [fremde]);
        var proposal = _solver.Solve(problem);

        Assert.Empty(proposal.Unscheduled);

        // Die fremde Ansetzung bleibt unangetastet stehen; alles Geplante rückt
        // dahinter.
        Assert.Contains(proposal.Assignments, a => a.MatchId == fremde.MatchId);
        Assert.All(
            proposal.Assignments.Where(a => a.MatchId != fremde.MatchId),
            a => Assert.True(a.PlannedStart >= At(16, 9, 20)));
    }

    [Fact]
    public void Ein_Zyklus_in_den_Abhaengigkeiten_wird_benannt()
    {
        // Kein Format erzeugt das: ein Sieger kommt immer aus einer früheren
        // Runde. Aus der Ablage kann es trotzdem kommen — und ohne die Schranke
        // liefe die Tiefensuche, bis der Aufrufstapel voll ist.
        var phaseId = Guid.NewGuid();
        var ersteId = Guid.NewGuid();
        var zweiteId = Guid.NewGuid();

        var erste = new Match(
            ersteId, _tournamentId, phaseId, 1, 1,
            ParticipantRef.FromWinnerOf(zweiteId), ParticipantRef.Open);

        var zweite = new Match(
            zweiteId, _tournamentId, phaseId, 1, 2,
            ParticipantRef.FromWinnerOf(ersteId), ParticipantRef.Open);

        var problem = new SchedulingProblem(
            [erste, zweite],
            new Dictionary<Guid, IReadOnlyList<Guid>>(),
            Courts(1),
            new Dictionary<Guid, TimeSpan>(),
            Rest,
            []);

        var fehler = Assert.Throws<DomainException>(() => _solver.Solve(problem));

        Assert.Contains("Zyklus", fehler.Message, StringComparison.Ordinal);
    }

    // --- Randfälle ---------------------------------------------------------

    private CourtAssignment Bestehend(
        Guid matchId,
        Guid courtId,
        DateTimeOffset? start = null,
        AssignmentSource source = AssignmentSource.Auto,
        int sequence = 1)
    {
        var assignment = new CourtAssignment(
            Guid.NewGuid(), _tournamentId, matchId, courtId, sequence,
            TimeSpan.FromMinutes(75), source);

        if (start is { } geplant)
        {
            assignment.PlanFor(geplant);
        }

        return assignment;
    }

    [Fact]
    public void Ohne_Matches_gibt_es_nichts_zu_planen()
    {
        var problem = new SchedulingProblem(
            [],
            new Dictionary<Guid, IReadOnlyList<Guid>>(),
            Courts(2),
            new Dictionary<Guid, TimeSpan>(),
            Rest,
            []);

        var proposal = _solver.Solve(problem);

        Assert.Empty(proposal.Assignments);
        Assert.Empty(proposal.Unscheduled);
    }

    [Fact]
    public void Ein_zu_kurzes_Fenster_nimmt_kein_Match_auf()
    {
        // Eine halbe Stunde Flutlicht reicht für kein Match. Das gehört gesagt,
        // und zwar je Match — ein leerer Vorschlag ohne Begründung wäre für die
        // Turnierleitung nicht zu deuten.
        var (phase, players) = Knockout(4);
        var kurz = new SchedulableCourt(
            Guid.NewGuid(), "Platz 1", false, [new TimeSlot(At(16, 20), At(16, 20, 30))]);

        var problem = Problem(phase, players, [kurz]);
        var proposal = _solver.Solve(problem);

        Assert.Empty(proposal.Assignments);
        Assert.Equal(phase.Matches.Count, proposal.Unscheduled.Count);
        Assert.All(proposal.Unscheduled, m => Assert.Contains("freies Fenster", m.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public void Ein_laufendes_Match_bleibt_stehen_wo_es_steht()
    {
        var (phase, players) = Knockout(4);
        var courts = Courts(2);
        var erstes = phase.Matches.First(m => m.Round == 1);

        var laufend = Bestehend(erstes.Id, courts[0].Id, At(16, 9));
        laufend.Call();
        laufend.Start(At(16, 9, 5));

        var problem = Problem(phase, players, courts, [laufend]);
        var proposal = _solver.Solve(problem);

        var unveraendert = proposal.Assignments.Single(a => a.MatchId == erstes.Id);

        Assert.Equal(At(16, 9), unveraendert.PlannedStart);
        Assert.Contains("Steht bereits am Platz", unveraendert.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Ansetzung_auf_einem_verschwundenen_Platz_wird_neu_gelegt()
    {
        // Der Platz ist stillgelegt worden, seit die Ansetzung entstand: sie
        // lässt sich nicht behalten, das Match bekommt einen neuen Termin.
        var (phase, players) = Knockout(4);
        var courts = Courts(2);
        var erstes = phase.Matches.First(m => m.Round == 1);

        var problem = Problem(
            phase,
            players,
            courts,
            [Bestehend(erstes.Id, Guid.NewGuid(), At(16, 9))]);

        var proposal = _solver.Solve(problem);

        var neu = proposal.Assignments.Single(a => a.MatchId == erstes.Id);

        Assert.Contains(neu.CourtId, courts.Select(c => c.Id));
        Assert.Empty(Violations(proposal, problem));
    }

    [Fact]
    public void Zwei_alte_Ansetzungen_desselben_Spielers_bleiben_nicht_beide()
    {
        // Beide sollen ihren Termin behalten, aber derselbe Mensch spielt in
        // beiden: die zweite muss weichen, sonst stünde er zweimal gleichzeitig
        // am Platz.
        var (phase, players) = Knockout(4);
        var courts = Courts(2);
        var erste = phase.Matches.Where(m => m.Round == 1).ToList();

        var doppelspieler = players[erste[0].Side1.EntryId!.Value][0];
        var geteilt = players.ToDictionary(
            paar => paar.Key,
            paar => paar.Key == erste[1].Side1.EntryId
                ? (IReadOnlyList<Guid>)[doppelspieler]
                : paar.Value);

        var problem = Problem(
            phase,
            geteilt,
            courts,
            [
                Bestehend(erste[0].Id, courts[0].Id, At(16, 9)),
                Bestehend(erste[1].Id, courts[1].Id, At(16, 9)),
            ]);

        var proposal = _solver.Solve(problem);

        Assert.Empty(Violations(proposal, problem));
        Assert.Contains(proposal.Assignments, a => a.Change == ProposalChange.Moved);
    }

    [Fact]
    public void Ein_verschobenes_Match_wird_als_verschoben_ausgewiesen()
    {
        // Die Auskunft, auf die es der Turnierleitung ankommt: was ändert sich
        // gegenüber dem Aushang, der schon hängt?
        var (phase, players) = Knockout(4);
        var courts = Courts(1);
        var erste = phase.Matches.Where(m => m.Round == 1).ToList();

        // Beide auf denselben Platz zur selben Zeit — eines muss weichen.
        var problem = Problem(
            phase,
            players,
            courts,
            [Bestehend(erste[0].Id, courts[0].Id, At(16, 20), sequence: 1)]);

        var proposal = _solver.Solve(problem);

        Assert.Contains(proposal.Assignments, a => a.MatchId == erste[0].Id);
        Assert.Empty(Violations(proposal, problem));
        Assert.True(proposal.Diff.Added + proposal.Diff.Moved > 0);
    }

    [Theory]
    // Ende 10:15, danach eine halbe Stunde Pause: 10:30 ist zu früh, 10:45 geht.
    [InlineData(10, 30, false)]
    [InlineData(10, 45, true)]
    public void Zwischen_zwei_Matches_desselben_Spielers_liegt_die_Pause(
        int stunde,
        int minute,
        bool bleibt)
    {
        var (phase, players) = Knockout(4);
        var courts = Courts(2);
        var erste = phase.Matches.Where(m => m.Round == 1).ToList();

        var doppelspieler = players[erste[0].Side1.EntryId!.Value][0];
        var geteilt = players.ToDictionary(
            paar => paar.Key,
            paar => paar.Key == erste[1].Side1.EntryId
                ? (IReadOnlyList<Guid>)[doppelspieler]
                : paar.Value);

        var problem = Problem(
            phase,
            geteilt,
            courts,
            [
                Bestehend(erste[0].Id, courts[0].Id, At(16, 9)),
                Bestehend(erste[1].Id, courts[1].Id, At(16, stunde, minute)),
            ]);

        var proposal = _solver.Solve(problem);
        var zweites = proposal.Assignments.Single(a => a.MatchId == erste[1].Id);

        Assert.Equal(bleibt, zweites.PlannedStart == At(16, stunde, minute));
        Assert.Empty(Violations(proposal, problem));
    }

    [Fact]
    public void Eine_Ansetzung_ausserhalb_der_Platzzeiten_wird_neu_gelegt()
    {
        // Die Platzzeit wurde nachträglich verkürzt: was jetzt davor oder danach
        // liegt, lässt sich nicht behalten.
        var (phase, players) = Knockout(4);
        var kurz = new SchedulableCourt(
            Guid.NewGuid(), "Platz 1", false, [new TimeSlot(At(16, 14), At(16, 22))]);

        var erstes = phase.Matches.First(m => m.Round == 1);

        var problem = Problem(phase, players, [kurz], [Bestehend(erstes.Id, kurz.Id, At(16, 9))]);
        var proposal = _solver.Solve(problem);

        var neu = proposal.Assignments.Single(a => a.MatchId == erstes.Id);

        Assert.True(neu.PlannedStart >= At(16, 14));
        Assert.Empty(Violations(proposal, problem));
    }

    [Fact]
    public void Ein_Platzwechsel_gilt_als_Verschiebung()
    {
        // Derselbe Beginn, anderer Platz: für den Aushang ist auch das eine
        // Änderung.
        var (phase, players) = Knockout(4);
        var courts = Courts(2);
        var erste = phase.Matches.Where(m => m.Round == 1).ToList();

        // Beide alten Ansetzungen liegen auf demselben Platz zur selben Zeit —
        // eine davon muss auf den anderen Platz.
        var problem = Problem(
            phase,
            players,
            courts,
            [
                Bestehend(erste[0].Id, courts[0].Id, At(16, 9)),
                Bestehend(erste[1].Id, courts[0].Id, At(16, 9), sequence: 2),
            ]);

        var proposal = _solver.Solve(problem);

        Assert.Empty(Violations(proposal, problem));
        Assert.Contains(
            proposal.Assignments,
            a => a.MatchId == erste[1].Id && a.Change != ProposalChange.Unchanged);
    }

    [Theory]
    // Das spätere Match beginnt 11:30. Ein früheres, das 11:15 endet, lässt
    // keine Pause; eines, das 10:15 endet, sehr wohl.
    [InlineData(10, 0, false)]
    [InlineData(9, 0, true)]
    public void Auch_das_frueher_liegende_Match_haelt_die_Pause_ein(int stunde, int minute, bool bleibt)
    {
        // Der umgekehrte Fall: nicht das spätere weicht, sondern das frühere
        // wird geprüft, nachdem das spätere schon steht. Ohne diese Richtung
        // stünde derselbe Mensch mit einer Viertelstunde Abstand zweimal am Platz.
        var (phase, players) = Knockout(4);
        var courts = Courts(2);
        var erste = phase.Matches.Where(m => m.Round == 1).ToList();

        var doppelspieler = players[erste[0].Side1.EntryId!.Value][0];
        var geteilt = players.ToDictionary(
            paar => paar.Key,
            paar => paar.Key == erste[1].Side1.EntryId
                ? (IReadOnlyList<Guid>)[doppelspieler]
                : paar.Value);

        var problem = Problem(
            phase,
            geteilt,
            courts,
            [
                Bestehend(erste[0].Id, courts[0].Id, At(16, 11, 30)),
                Bestehend(erste[1].Id, courts[1].Id, At(16, stunde, minute)),
            ]);

        var proposal = _solver.Solve(problem);
        var frueheres = proposal.Assignments.Single(a => a.MatchId == erste[1].Id);

        Assert.Equal(bleibt, frueheres.PlannedStart == At(16, stunde, minute));
        Assert.Empty(Violations(proposal, problem));
    }

    [Fact]
    public void Eine_Ansetzung_ohne_Zeit_bekommt_eine_und_gilt_als_verschoben()
    {
        // Von Hand auf einen Platz gesetzt, ohne Zeit: der Solver trägt sie nach
        // und weist sie als Änderung aus — im alten Aushang stand keine.
        var (phase, players) = Knockout(4);
        var courts = Courts(2);
        var erstes = phase.Matches.First(m => m.Round == 1);

        var problem = Problem(
            phase,
            players,
            courts,
            [Bestehend(erstes.Id, courts[0].Id, start: null, source: AssignmentSource.Auto)]);

        var proposal = _solver.Solve(problem);
        var nachgetragen = proposal.Assignments.Single(a => a.MatchId == erstes.Id);

        Assert.NotEqual(default, nachgetragen.PlannedStart);
        Assert.Equal(ProposalChange.Moved, nachgetragen.Change);
    }
}
