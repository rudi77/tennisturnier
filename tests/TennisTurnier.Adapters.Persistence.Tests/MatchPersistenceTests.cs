using Microsoft.EntityFrameworkCore;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Adapters.Persistence.Tests;

public sealed class MatchPersistenceTests : IAsyncLifetime
{
    private static readonly MatchFormat Standard = new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6);

    private readonly SqliteTestDatabase _database = new();

    private Guid _tournamentId;
    private Guid _courtId;
    private readonly List<Guid> _entryIds = [];

    public async Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        await using var db = _database.NewContext();

        var template = new FormatTemplate(Guid.NewGuid(), Guid.NewGuid(), BuiltInFormats.Knockout);
        db.FormatTemplates.Add(template);

        var tournament = new Tournament(
            Guid.NewGuid(),
            "Clubmeisterschaft",
            new Venue("TC Musterstadt", null, "Musterstadt", "Europe/Vienna"),
            Discipline.Singles,
            new DateOnly(2026, 5, 16),
            new DateOnly(2026, 5, 17),
            template.Id);

        var court = tournament.AddCourt(
            Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

        tournament.OpenRegistration();
        for (var i = 0; i < 4; i++)
        {
            var participant = Participant.Single(Guid.NewGuid(), Guid.NewGuid(), $"Spielerin {i + 1}");
            db.Participants.Add(participant);

            var entry = tournament.Enter(Guid.NewGuid(), participant.Id, seed: i + 1);
            tournament.Accept(entry.Id);
            _entryIds.Add(entry.Id);
        }

        tournament.CloseRegistration();
        tournament.GenerateDraw(template.Definition, template.Version);
        db.Tournaments.Add(tournament);

        await db.SaveChangesAsync();

        _tournamentId = tournament.Id;
        _courtId = court.Id;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private Phase BuildPhase()
    {
        var phase = new Phase(Guid.NewGuid(), _tournamentId, 1, PhaseFormatKind.Knockout, "Hauptfeld");
        var entries = _entryIds.Select((id, i) => new SeededEntry(id, i + 1, $"Spielerin {i + 1}")).ToList();

        var state = new PhaseState(
            new PhaseDefinition { Ordinal = 1, Format = PhaseFormatKind.Knockout },
            entries,
            phase.Matches);

        phase.AddPairings(new KnockoutFormat().GeneratePairings(state));
        return phase;
    }

    private async Task<Guid> SeedPhaseAsync()
    {
        await using var db = _database.NewContext();
        var phase = BuildPhase();
        db.Phases.Add(phase);
        await db.SaveChangesAsync();

        return phase.Id;
    }

    [Fact]
    public async Task Ein_Turnierbaum_uebersteht_den_Neuaufbau()
    {
        var phaseId = await SeedPhaseAsync();

        await using var db = _database.NewContext();
        var phase = await db.Phases.SingleAsync(p => p.Id == phaseId);

        Assert.Equal(3, phase.Matches.Count);
        Assert.Equal(2, phase.Matches.Count(m => m.Round == 1));
        Assert.Equal("Finale", phase.Matches.Single(m => m.Round == 2).Label);
    }

    [Fact]
    public async Task Die_Herkunft_einer_Match_Seite_ueberlebt_den_Neuaufbau()
    {
        // Der Kern von ADR-0001 auf der Persistenzebene: ginge „Sieger aus
        // Match 7" beim Speichern verloren, ließe sich der Baum nach einem
        // Neustart nicht mehr fortschreiben.
        var phaseId = await SeedPhaseAsync();

        await using var db = _database.NewContext();
        var phase = await db.Phases.SingleAsync(p => p.Id == phaseId);

        var firstRound = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();
        var final = phase.Matches.Single(m => m.Round == 2);

        Assert.Equal(ParticipantRef.FromWinnerOf(firstRound[0].Id), final.Side1.Origin);
        Assert.Equal(ParticipantRef.FromWinnerOf(firstRound[1].Id), final.Side2.Origin);
        Assert.Null(final.Side1.EntryId);
    }

    [Fact]
    public async Task Eine_feststehende_Seite_traegt_ihre_Meldung()
    {
        var phaseId = await SeedPhaseAsync();

        await using var db = _database.NewContext();
        var phase = await db.Phases.SingleAsync(p => p.Id == phaseId);
        var match = phase.Matches.First(m => m.Round == 1);

        Assert.Contains(match.Side1.EntryId, _entryIds.Cast<Guid?>());
        Assert.IsType<ParticipantRef.Entry>(match.Side1.Origin);
    }

    [Theory]
    [InlineData("E")]
    [InlineData("W")]
    [InlineData("L")]
    [InlineData("G")]
    [InlineData("BYE")]
    [InlineData("OPEN")]
    public void Jede_Art_von_Referenz_ueberlebt_Kodierung_und_Auswertung(string kind)
    {
        // Die Kodierung ist die einzige Stelle, an der die Struktur des
        // Turnierbaums die Sprache verlässt. Läuft sie auseinander, sind
        // gespeicherte Turniere unlesbar.
        ParticipantRef original = kind switch
        {
            "E" => ParticipantRef.Of(Guid.NewGuid()),
            "W" => ParticipantRef.FromWinnerOf(Guid.NewGuid()),
            "L" => ParticipantRef.FromLoserOf(Guid.NewGuid()),
            "G" => ParticipantRef.FromGroupPosition(Guid.NewGuid(), "Gruppe: A", 2),
            "BYE" => ParticipantRef.ByeSlot,
            _ => ParticipantRef.Open,
        };

        Assert.Equal(original, ParticipantRef.Parse(original.Encode()));
    }

    [Fact]
    public async Task Ein_Ergebnis_uebersteht_den_Neuaufbau()
    {
        var phaseId = await SeedPhaseAsync();
        var score = Score.Played([new SetScore(7, 6, 5), new SetScore(3, 6), new SetScore(10, 8)], Standard);

        await using (var db = _database.NewContext())
        {
            var phase = await db.Phases.SingleAsync(p => p.Id == phaseId);
            var match = phase.Matches.First(m => m.Round == 1);
            phase.RecordResult(match.Id, score);
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            var phase = await db.Phases.SingleAsync(p => p.Id == phaseId);
            var stored = phase.Matches.First(m => m.Round == 1).Score;

            Assert.NotNull(stored);
            Assert.Equal(MatchOutcome.Normal, stored.Outcome);
            Assert.Equal(3, stored.CompletedSets.Count);
            Assert.Equal(5, stored.CompletedSets[0].TiebreakPoints);
            Assert.Equal(score.WinnerSide, stored.WinnerSide);
        }
    }

    [Fact]
    public async Task Eine_Aufgabe_behaelt_den_abgebrochenen_Satz()
    {
        var phaseId = await SeedPhaseAsync();

        await using (var db = _database.NewContext())
        {
            var phase = await db.Phases.SingleAsync(p => p.Id == phaseId);
            var match = phase.Matches.First(m => m.Round == 1);
            phase.RecordResult(
                match.Id,
                Score.Retired([new SetScore(6, 4)], new SetScore(2, 1), retiringSide: 2, Standard));

            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            var stored = (await db.Phases.SingleAsync(p => p.Id == phaseId))
                .Matches.First(m => m.Round == 1).Score;

            Assert.Equal(MatchOutcome.Retirement, stored!.Outcome);
            Assert.Single(stored.CompletedSets);
            Assert.Equal(new SetScore(2, 1), stored.AbandonedSet);
            Assert.Equal(1, stored.SetsWonBy(1));
        }
    }

    [Fact]
    public async Task Die_Propagation_ueberlebt_den_Neuaufbau()
    {
        var phaseId = await SeedPhaseAsync();
        Guid expectedWinner;

        await using (var db = _database.NewContext())
        {
            var phase = await db.Phases.SingleAsync(p => p.Id == phaseId);
            var match = phase.Matches.OrderBy(m => m.Round).ThenBy(m => m.Position).First();
            phase.RecordResult(match.Id, Score.Played([new SetScore(6, 4), new SetScore(6, 2)], Standard));
            expectedWinner = match.WinnerEntryId!.Value;
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            var final = (await db.Phases.SingleAsync(p => p.Id == phaseId))
                .Matches.Single(m => m.Round == 2);

            Assert.Equal(expectedWinner, final.Side1.EntryId);
        }
    }

    [Fact]
    public async Task Eine_Platzzuweisung_uebersteht_den_Neuaufbau()
    {
        var phaseId = await SeedPhaseAsync();
        Guid assignmentId;
        var start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.FromHours(2));

        await using (var db = _database.NewContext())
        {
            var match = (await db.Phases.SingleAsync(p => p.Id == phaseId)).Matches.First(m => m.Round == 1);

            var assignment = new CourtAssignment(
                Guid.NewGuid(), _tournamentId, match.Id, _courtId, 1,
                TimeSpan.FromMinutes(90), AssignmentSource.Manual);

            assignment.PlanFor(start);
            assignment.PromiseNotBefore(start);

            db.CourtAssignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
        }

        await using (var db = _database.NewContext())
        {
            var stored = await db.CourtAssignments.SingleAsync(a => a.Id == assignmentId);

            Assert.Equal(_courtId, stored.CourtId);
            Assert.Equal(1, stored.SequenceOnCourt);
            Assert.Equal(TimeSpan.FromMinutes(90), stored.EstimatedDuration);
            Assert.Equal(start, stored.PlannedStart);
            Assert.Equal(start, stored.EarliestStart);
            Assert.Equal(AssignmentSource.Manual, stored.Source);
            Assert.Equal(AssignmentStatus.Planned, stored.Status);
        }
    }

    [Fact]
    public async Task Ein_Ergebnis_aus_einem_abweichenden_Satzformat_laesst_sich_wieder_laden()
    {
        // Regression: beim Zurücklesen wurde das Matchformat geraten statt
        // geladen — es steht in der eingefrorenen Formatdefinition, die dabei
        // nicht zur Hand ist. Ein Turnier mit Kurzsätzen bis 4 speicherte damit
        // ein gültiges 5:4, das gegen die geratenen Regeln eines Satzes bis 6
        // scheiterte: das Match ließ sich nicht mehr laden.
        var shortSets = new MatchFormat(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 4);
        var phaseId = await SeedPhaseAsync();
        var score = Score.Played([new SetScore(5, 4, 3), new SetScore(4, 1)], shortSets);

        await using (var db = _database.NewContext())
        {
            var phase = await db.Phases.SingleAsync(p => p.Id == phaseId);
            phase.RecordResult(phase.Matches.First(m => m.Round == 1).Id, score);
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            var stored = (await db.Phases.SingleAsync(p => p.Id == phaseId))
                .Matches.First(m => m.Round == 1).Score;

            Assert.NotNull(stored);
            Assert.Equal(2, stored.CompletedSets.Count);
            Assert.Equal(new SetScore(5, 4, 3), stored.CompletedSets[0]);
            Assert.Equal(1, stored.WinnerSide);
        }
    }

    [Fact]
    public async Task Ein_unveraenderter_Turnierbaum_erzeugt_keinen_Schreibvorgang()
    {
        // Ohne eigene Wertvergleicher für Referenz und Ergebnis hielte EF Core
        // jeden geladenen Baum für verändert und schriebe ihn bei jedem Laden
        // zurück — auf SQLite, das datenbankweit serialisiert, spürbar.
        //
        // Der Baum trägt dafür ein Ergebnis mit beiden Satzlisten: ohne
        // eingetragenes Ergebnis liefe der Vergleicher des Ergebnisses nie, und
        // der Test prüfte nur die halbe Behauptung.
        var phaseId = await SeedPhaseAsync();

        await using (var seed = _database.NewContext())
        {
            var phase = await seed.Phases.SingleAsync(p => p.Id == phaseId);
            phase.RecordResult(
                phase.Matches.First(m => m.Round == 1).Id,
                Score.Retired([new SetScore(6, 4)], new SetScore(2, 1), retiringSide: 2, Standard));

            await seed.SaveChangesAsync();
        }

        await using var db = _database.NewContext();
        _ = await db.Phases.SingleAsync(p => p.Id == phaseId);

        Assert.Equal(0, await db.SaveChangesAsync());
    }

    [Fact]
    public async Task Zwei_gleichzeitige_Ergebniseintragungen_werden_erkannt()
    {
        // Zwei Schiedsrichter am selben Match sind am Turniertag der Normalfall.
        var phaseId = await SeedPhaseAsync();

        await using var first = _database.NewContext();
        await using var second = _database.NewContext();

        var a = await first.Phases.SingleAsync(p => p.Id == phaseId);
        var b = await second.Phases.SingleAsync(p => p.Id == phaseId);

        var matchA = a.Matches.OrderBy(m => m.Round).ThenBy(m => m.Position).First();
        var matchB = b.Matches.Single(m => m.Id == matchA.Id);

        a.RecordResult(matchA.Id, Score.Played([new SetScore(6, 4), new SetScore(6, 2)], Standard));
        await first.SaveChangesAsync();

        b.RecordResult(matchB.Id, Score.Played([new SetScore(4, 6), new SetScore(2, 6)], Standard));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
