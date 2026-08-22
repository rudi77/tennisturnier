using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Domain.Tests.Scheduling;

public sealed class ScheduleValidatorTests
{
    private static readonly MatchFormat Standard = new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6);

    private readonly ScheduleValidator _validator = new();
    private readonly Guid _tournamentId = Guid.NewGuid();
    private readonly Guid _courtA = Guid.NewGuid();
    private readonly Guid _courtB = Guid.NewGuid();

    private static DateTimeOffset At(int hour, int minute = 0) =>
        new(2026, 5, 16, hour, minute, 0, TimeSpan.Zero);

    private static readonly TimeSlot WholeDay = new(At(8), At(22));

    /// <summary>Vier Teilnehmer, jeweils mit genau einem Spieler.</summary>
    private readonly List<Guid> _entries = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
    private readonly List<Guid> _players = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    private Phase BuildPhase()
    {
        var phase = new Phase(Guid.NewGuid(), _tournamentId, 1, PhaseFormatKind.Knockout);
        var entries = _entries.Select((id, i) => new SeededEntry(id, i + 1, $"Spielerin {i + 1}")).ToList();

        var state = new PhaseState(
            new PhaseDefinition { Ordinal = 1, Format = PhaseFormatKind.Knockout },
            entries,
            phase.Matches);

        phase.AddPairings(new KnockoutFormat().GeneratePairings(state));
        return phase;
    }

    private SchedulingContext ContextFor(
        Phase phase,
        TimeSpan? rest = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? playersByEntry = null) =>
        new(
            phase.Matches,
            playersByEntry ?? _entries
                .Select((id, i) => (Entry: id, Player: _players[i]))
                .ToDictionary(x => x.Entry, x => (IReadOnlyList<Guid>)[x.Player]),
            new Dictionary<Guid, IReadOnlyList<TimeSlot>>
            {
                [_courtA] = [WholeDay],
                [_courtB] = [WholeDay],
            },
            rest ?? TimeSpan.FromMinutes(30));

    private CourtAssignment Assign(
        Match match,
        Guid courtId,
        DateTimeOffset start,
        int sequence = 1,
        TimeSpan? duration = null)
    {
        var assignment = new CourtAssignment(
            Guid.NewGuid(),
            _tournamentId,
            match.Id,
            courtId,
            sequence,
            duration ?? TimeSpan.FromMinutes(90),
            AssignmentSource.Manual);

        assignment.PlanFor(start);
        return assignment;
    }

    [Fact]
    public void Ein_sauberer_Plan_hat_keine_Verstoesse()
    {
        var phase = BuildPhase();
        var firstRound = phase.Matches.Where(m => m.Round == 1).ToList();

        var assignments = new[]
        {
            Assign(firstRound[0], _courtA, At(9)),
            Assign(firstRound[1], _courtB, At(9)),
        };

        Assert.Empty(_validator.Validate(assignments, ContextFor(phase)));
    }

    [Fact]
    public void Zwei_Matches_gleichzeitig_auf_einem_Platz_werden_erkannt()
    {
        var phase = BuildPhase();
        var firstRound = phase.Matches.Where(m => m.Round == 1).ToList();

        var assignments = new[]
        {
            Assign(firstRound[0], _courtA, At(9)),
            Assign(firstRound[1], _courtA, At(10)),
        };

        var violations = _validator.Validate(assignments, ContextFor(phase));

        Assert.Contains(violations, v => v.Constraint == ScheduleConstraint.CourtDoubleBooked);
    }

    [Fact]
    public void Aneinander_grenzende_Matches_auf_einem_Platz_sind_in_Ordnung()
    {
        var phase = BuildPhase();
        var firstRound = phase.Matches.Where(m => m.Round == 1).ToList();

        var assignments = new[]
        {
            Assign(firstRound[0], _courtA, At(9)),
            Assign(firstRound[1], _courtA, At(10, 30), sequence: 2),
        };

        Assert.Empty(_validator.Validate(assignments, ContextFor(phase)));
    }

    [Fact]
    public void Ein_Spieler_kann_nicht_zweimal_gleichzeitig_antreten()
    {
        // Der Fall entsteht durch Einzel und Doppel: derselbe Spieler steckt in
        // zwei verschiedenen Teilnehmern. Über die Meldung allein wäre das nicht
        // zu erkennen.
        var phase = BuildPhase();
        var firstRound = phase.Matches.Where(m => m.Round == 1).ToList();

        var assignments = new[]
        {
            Assign(firstRound[0], _courtA, At(9)),
            Assign(firstRound[1], _courtB, At(9)),
        };

        var violations = _validator.Validate(
            assignments, ContextFor(phase, playersByEntry: SharedPlayerAcrossMatches()));

        Assert.Contains(violations, v => v.Constraint == ScheduleConstraint.PlayerDoubleBooked);
    }

    [Fact]
    public void Eine_zu_kurze_Pause_zwischen_zwei_Matches_wird_erkannt()
    {
        var phase = BuildPhase();
        var firstRound = phase.Matches.Where(m => m.Round == 1).ToList();

        var assignments = new[]
        {
            Assign(firstRound[0], _courtA, At(9)),
            Assign(firstRound[1], _courtB, At(10, 45)),
        };

        var violations = _validator.Validate(
            assignments, ContextFor(phase, playersByEntry: SharedPlayerAcrossMatches()));

        Assert.Contains(violations, v => v.Constraint == ScheduleConstraint.PlayerRestPeriod);
    }

    [Fact]
    public void Eine_ausreichende_Pause_ist_in_Ordnung()
    {
        var phase = BuildPhase();
        var firstRound = phase.Matches.Where(m => m.Round == 1).ToList();

        var assignments = new[]
        {
            Assign(firstRound[0], _courtA, At(9)),
            Assign(firstRound[1], _courtB, At(11)),
        };

        Assert.Empty(_validator.Validate(
            assignments, ContextFor(phase, playersByEntry: SharedPlayerAcrossMatches())));
    }

    [Fact]
    public void Ein_Match_ausserhalb_der_Platzverfuegbarkeit_wird_erkannt()
    {
        var phase = BuildPhase();
        var match = phase.Matches.First(m => m.Round == 1);

        var violations = _validator.Validate([Assign(match, _courtA, At(21, 30))], ContextFor(phase));

        Assert.Contains(violations, v => v.Constraint == ScheduleConstraint.CourtUnavailable);
    }

    [Fact]
    public void Ein_Match_das_ueber_eine_Sperre_hinweg_liegt_wird_erkannt()
    {
        // Es passt in keines der beiden Fenster ganz hinein — über die Sperre
        // hinweg zu planen hieße, während der Sperre zu spielen.
        var phase = BuildPhase();
        var match = phase.Matches.First(m => m.Round == 1);

        var context = new SchedulingContext(
            phase.Matches,
            _entries.Select((id, i) => (id, _players[i]))
                .ToDictionary(x => x.id, x => (IReadOnlyList<Guid>)[x.Item2]),
            new Dictionary<Guid, IReadOnlyList<TimeSlot>>
            {
                [_courtA] = [new TimeSlot(At(8), At(14)), new TimeSlot(At(18), At(22))],
            },
            TimeSpan.FromMinutes(30));

        var violations = _validator.Validate([Assign(match, _courtA, At(13, 30))], context);

        Assert.Contains(violations, v => v.Constraint == ScheduleConstraint.CourtUnavailable);
    }

    [Fact]
    public void Ein_unbekannter_Platz_gilt_als_nicht_verfuegbar()
    {
        var phase = BuildPhase();
        var match = phase.Matches.First(m => m.Round == 1);

        var violations = _validator.Validate([Assign(match, Guid.NewGuid(), At(9))], ContextFor(phase));

        Assert.Contains(violations, v => v.Constraint == ScheduleConstraint.CourtUnavailable);
    }

    [Fact]
    public void Ein_Folgematch_darf_nicht_vor_seinem_Vorgaenger_enden()
    {
        var phase = BuildPhase();
        var firstRound = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();
        var final = phase.Matches.Single(m => m.Round == 2);

        var assignments = new[]
        {
            Assign(firstRound[0], _courtA, At(9)),
            Assign(firstRound[1], _courtB, At(9)),
            Assign(final, _courtA, At(10), sequence: 2),
        };

        var violations = _validator.Validate(assignments, ContextFor(phase));

        Assert.Contains(violations, v => v.Constraint == ScheduleConstraint.DependencyOrder);
    }

    [Fact]
    public void Ein_Folgematch_nach_beiden_Vorgaengern_ist_in_Ordnung()
    {
        var phase = BuildPhase();
        var firstRound = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();
        var final = phase.Matches.Single(m => m.Round == 2);

        // Beide Halbfinals sind um 10:30 zu Ende; das Finale beginnt um 12:00,
        // was auch die Mindestpause der Spieler wahrt.
        var assignments = new[]
        {
            Assign(firstRound[0], _courtA, At(9)),
            Assign(firstRound[1], _courtB, At(9)),
            Assign(final, _courtA, At(12), sequence: 2),
        };

        Assert.Empty(_validator.Validate(assignments, ContextFor(phase)));
    }

    [Fact]
    public void Ein_Match_ohne_feststehende_Teilnehmer_darf_eingeplant_werden()
    {
        // Im Planungsmodus steht der ganze Baum, bevor ein Ball gespielt ist.
        // Ein Finale ohne bekannte Teilnehmer einzuplanen ist gerade der Zweck
        // der Übung — die Machbarkeit soll vorab geprüft werden.
        var phase = BuildPhase();
        var final = phase.Matches.Single(m => m.Round == 2);

        Assert.Empty(_validator.Validate([Assign(final, _courtA, At(12))], ContextFor(phase)));
    }

    [Fact]
    public void Eine_Zuweisung_ohne_Planzeit_bleibt_ungeprueft()
    {
        // Im Turniertagbetrieb gibt es keine Planzeiten, sondern Warteschlangen —
        // die zeitbezogenen Constraints haben dort nichts zu prüfen.
        var phase = BuildPhase();
        var match = phase.Matches.First(m => m.Round == 1);

        var assignment = new CourtAssignment(
            Guid.NewGuid(), _tournamentId, match.Id, _courtA, 1, TimeSpan.FromMinutes(90), AssignmentSource.Manual);

        Assert.Empty(_validator.Validate([assignment], ContextFor(phase)));
    }

    /// <summary>
    /// Ein Spieler, der in zwei verschiedenen Erstrundenmatches steckt — der
    /// Fall „im Einzel und im Doppel gemeldet". Bei vier Gesetzten paart
    /// SeedOrder 1 gegen 4 und 2 gegen 3, also liegen Meldung 1 und Meldung 2
    /// in verschiedenen Matches.
    /// </summary>
    private IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> SharedPlayerAcrossMatches()
    {
        var shared = _players[0];

        return new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [_entries[0]] = [shared],
            [_entries[1]] = [shared],
            [_entries[2]] = [_players[2]],
            [_entries[3]] = [_players[3]],
        };
    }

    [Fact]
    public void Eine_Zuweisung_ohne_Match_wird_uebergangen()
    {
        // Ein Match, das der Prüfer nicht kennt — etwa weil es beim Neuauslosen
        // verschwunden ist. Der Plan darf daran nicht scheitern: die verwaiste
        // Zuweisung trägt keine Spieler bei und begründet auch keine Abhängigkeit.
        var phase = BuildPhase();
        var erste = phase.Matches.Where(m => m.Round == 1).ToList();

        var verwaist = new CourtAssignment(
            Guid.NewGuid(),
            _tournamentId,
            Guid.NewGuid(),
            _courtA,
            2,
            TimeSpan.FromMinutes(90),
            AssignmentSource.Manual);

        verwaist.PlanFor(At(14));

        var verstoesse = _validator.Validate(
            [Assign(erste[0], _courtA, At(9)), verwaist],
            ContextFor(phase));

        Assert.Empty(verstoesse);
    }
}
