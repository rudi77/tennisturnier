using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Ein vollständiges K.-o.-Turnier über die API — die Abnahmebedingung für M3.
/// </summary>
public sealed class KnockoutTournamentApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public KnockoutTournamentApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>Turnier mit ausgelostem Baum über die angegebene Teilnehmerzahl.</summary>
    private async Task<(HttpClient Client, Guid CourtId, Guid TournamentId)> DrawnTournamentAsync(
        int participants,
        bool seedAll = false)
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            "ko-admin",
            new TurnierWunsch
            {
                Anlage = "TC KO",
                Teilnehmer = participants,
                Setzen = seedAll,
                Plaetze = 1,
            });

        return (aufbau.Admin, aufbau.CourtIds.Single(), aufbau.TournamentId);
    }

    private static async Task<List<PhaseDetail>> PhasesAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json))!;

    /// <summary>
    /// Das Finale. Die mitgelieferte Vorlage enthält ein Spiel um Platz 3, das in
    /// derselben Runde steht — die Runde allein genügt also nicht.
    /// </summary>
    private static MatchDetail FinalOf(PhaseDetail phase) =>
        phase.Matches.Single(m => m.Label == "Finale");

    private static MatchDetail ThirdPlaceOf(PhaseDetail phase) =>
        phase.Matches.Single(m => m.Label == "Spiel um Platz 3");

    [Fact]
    public async Task Die_Auslosung_erzeugt_den_vollstaendigen_Baum()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync(16);

        var phase = Assert.Single(await PhasesAsync(client, tournamentId));

        // 15 Matches für 16 Teilnehmer, plus das Spiel um Platz 3 aus der Vorlage.
        Assert.Equal(16, phase.Matches.Count);
        Assert.Equal(8, phase.Matches.Count(m => m.Round == 1));
        Assert.Equal(4, FinalOf(phase).Round);
        Assert.NotNull(ThirdPlaceOf(phase));
    }

    [Fact]
    public async Task Das_Bracket_ist_lesbar_bevor_gespielt_wurde()
    {
        // Grundlage der öffentlichen Vorschau aus ADR-0003: die späteren Runden
        // nennen ihre Herkunft im Klartext, obwohl noch niemand feststeht.
        var (client, _, tournamentId) = await DrawnTournamentAsync(8);

        var phase = Assert.Single(await PhasesAsync(client, tournamentId));
        var final = FinalOf(phase);

        Assert.Equal(MatchStatus.Pending, final.Status);
        Assert.Null(final.Side1.EntryId);
        Assert.StartsWith("Sieger aus", final.Side1.Origin, StringComparison.Ordinal);

        var firstRound = phase.Matches.First(m => m.Round == 1);
        Assert.Equal(MatchStatus.Ready, firstRound.Status);
        Assert.NotNull(firstRound.Side1.ParticipantName);
    }

    [Fact]
    public async Task Freilose_entscheiden_sich_beim_Auslosen()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync(5, seedAll: true);

        var phase = Assert.Single(await PhasesAsync(client, tournamentId));
        var firstRound = phase.Matches.Where(m => m.Round == 1).ToList();

        Assert.Equal(4, firstRound.Count);
        Assert.Equal(3, firstRound.Count(m => m.Score?.Outcome == MatchOutcome.Bye));
    }

    [Fact]
    public async Task Ein_Ergebnis_bringt_den_Sieger_in_die_naechste_Runde()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync(4);
        var phase = Assert.Single(await PhasesAsync(client, tournamentId));
        var match = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).First();

        var response = await client.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = Assert.Single(await PhasesAsync(client, tournamentId));
        var final = FinalOf(updated);

        Assert.Equal(match.Side1.EntryId, final.Side1.EntryId);
        Assert.Equal(match.Side1.ParticipantName, final.Side1.ParticipantName);
    }

    [Fact]
    public async Task Das_erste_Ergebnis_startet_das_Turnier()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync(4);
        var phase = Assert.Single(await PhasesAsync(client, tournamentId));

        await client.PutAsJsonAsync(
            $"/api/matches/{phase.Matches.First(m => m.Round == 1).Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        var detail = await client.GetFromJsonAsync<TournamentDetail>($"/api/tournaments/{tournamentId}", Json);

        Assert.Equal(TournamentState.InProgress, detail!.State);
    }

    [Fact]
    public async Task Ein_16er_Turnier_laesst_sich_bis_zum_Finale_durchspielen()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync(16);

        await PlayOutAsync(client, tournamentId);

        var phase = Assert.Single(await PhasesAsync(client, tournamentId));
        Assert.Equal(PhaseStatus.Completed, phase.Status);
        Assert.All(phase.Matches, m => Assert.Equal(MatchStatus.Finished, m.Status));

        var standings = await client.GetFromJsonAsync<StandingsDetail>(
            $"/api/tournaments/{tournamentId}/phases/{phase.Id}/standings", Json);

        Assert.Equal(16, standings!.Places.Count);
        Assert.Equal(1, standings.Places[0].Rank);

        // Der Sieger hat vier Runden gewonnen und keine verloren.
        Assert.Equal(4, standings.Places[0].Won);
        Assert.Equal(0, standings.Places[0].Lost);
    }

    [Fact]
    public async Task Aufgabe_und_Nichtantreten_laufen_ueber_dieselbe_Kette()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync(4);
        var phase = Assert.Single(await PhasesAsync(client, tournamentId));
        var firstRound = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();

        var retirement = await client.PutAsJsonAsync(
            $"/api/matches/{firstRound[0].Id}/result",
            new RecordResultRequest(
                MatchOutcome.Retirement,
                Sets: [new SetScore(6, 4)],
                AbandonedSet: new SetScore(2, 1),
                AffectedSide: 2),
            Json);
        Assert.Equal(HttpStatusCode.NoContent, retirement.StatusCode);

        var walkover = await client.PutAsJsonAsync(
            $"/api/matches/{firstRound[1].Id}/result",
            new RecordResultRequest(MatchOutcome.Walkover, AffectedSide: 1),
            Json);
        Assert.Equal(HttpStatusCode.NoContent, walkover.StatusCode);

        var updated = Assert.Single(await PhasesAsync(client, tournamentId));
        var final = FinalOf(updated);

        Assert.Equal(MatchStatus.Ready, final.Status);
        Assert.Equal(firstRound[0].Side1.EntryId, final.Side1.EntryId);
        Assert.Equal(firstRound[1].Side2.EntryId, final.Side2.EntryId);
    }

    [Fact]
    public async Task Eine_Ergebniskorrektur_scheitert_am_gespielten_Folgematch()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync(4);
        await PlayOutAsync(client, tournamentId);

        var phase = Assert.Single(await PhasesAsync(client, tournamentId));
        var firstRound = phase.Matches.First(m => m.Round == 1);

        var response = await client.DeleteAsync($"/api/matches/{firstRound.Id}/result");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Eine_Korrektur_von_hinten_nach_vorn_ist_moeglich()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync(4);
        await PlayOutAsync(client, tournamentId);

        var phase = Assert.Single(await PhasesAsync(client, tournamentId));
        var final = FinalOf(phase);
        var thirdPlace = ThirdPlaceOf(phase);
        var firstRound = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).First();

        // Von hinten nach vorn: erst Finale und Spiel um Platz 3, dann das
        // Halbfinale, aus dem beide ihre Teilnehmer beziehen.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/matches/{final.Id}/result")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/matches/{thirdPlace.Id}/result")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/matches/{firstRound.Id}/result")).StatusCode);

        var updated = Assert.Single(await PhasesAsync(client, tournamentId));
        Assert.Null(updated.Matches.Single(m => m.Id == firstRound.Id).Score);
        Assert.Null(FinalOf(updated).Side1.EntryId);
    }

    [Fact]
    public async Task Das_Zuruecknehmen_der_Auslosung_verwirft_auch_den_Baum()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync(4);
        Assert.NotEmpty(await PhasesAsync(client, tournamentId));

        await client.PostAsync($"/api/tournaments/{tournamentId}/registration/reopen", null);

        Assert.Empty(await PhasesAsync(client, tournamentId));
    }

    [Fact]
    public async Task Ein_Match_laesst_sich_einem_Platz_zuweisen()
    {
        var (client, courtId, tournamentId) = await DrawnTournamentAsync(4);
        var phase = Assert.Single(await PhasesAsync(client, tournamentId));
        var match = phase.Matches.First(m => m.Round == 1);

        var response = await client.PostAsJsonAsync(
            $"/api/matches/{match.Id}/court",
            new AssignCourtRequest(
                courtId,
                SequenceOnCourt: 1,
                PlannedStart: null,
                EarliestStart: new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.FromHours(2)),
                EstimatedDuration: TimeSpan.FromMinutes(90)),
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AssignCourtResult>(Json);
        Assert.NotNull(result);

        var updated = Assert.Single(await PhasesAsync(client, tournamentId));
        var assignment = updated.Matches.First(m => m.Id == match.Id).Assignment;

        Assert.NotNull(assignment);
        Assert.Equal("Platz 1", assignment.CourtName);
        Assert.Equal(1, assignment.SequenceOnCourt);
    }

    [Fact]
    public async Task Eine_zweite_Zuweisung_ersetzt_die_erste()
    {
        var (client, courtId, tournamentId) = await DrawnTournamentAsync(4);
        var phase = Assert.Single(await PhasesAsync(client, tournamentId));
        var match = phase.Matches.First(m => m.Round == 1);

        async Task<AssignCourtResult> AssignAsync(int sequence) =>
            (await (await client.PostAsJsonAsync(
                $"/api/matches/{match.Id}/court",
                new AssignCourtRequest(courtId, sequence, null, null, null),
                Json)).Content.ReadFromJsonAsync<AssignCourtResult>(Json))!;

        var first = await AssignAsync(1);
        var second = await AssignAsync(3);

        var updated = Assert.Single(await PhasesAsync(client, tournamentId));

        Assert.Equal(3, updated.Matches.First(m => m.Id == match.Id).Assignment!.SequenceOnCourt);

        // Dieselbe Zuweisung, nicht eine zweite: nur auf einer bestehenden Zeile
        // wirkt der Zähler für Nebenläufigkeit. Wurde sie stattdessen gelöscht und
        // neu angelegt, liefen zwei gleichzeitige Zuweisungen beide durch und
        // hinterließen zwei aktive Zeilen für dasselbe Match.
        Assert.Equal(first.AssignmentId, second.AssignmentId);
    }

    [Fact]
    public async Task Eine_bereits_vergebene_Zuweisung_bleibt_bei_gleichzeitiger_Aenderung_eindeutig()
    {
        // Regression: die zweite Zuweisung war ein Löschen und Neuanlegen. Zwei
        // gleichzeitige Aufrufe lasen beide dieselbe alte Zuweisung, löschten sie
        // beide und legten je eine neue an — am Ende stand das Match zweimal in
        // der Platzbelegung, ohne dass irgendwer einen Konflikt gesehen hätte.
        var (client, courtId, tournamentId) = await DrawnTournamentAsync(4);
        var phase = Assert.Single(await PhasesAsync(client, tournamentId));
        var match = phase.Matches.First(m => m.Round == 1);

        await client.PostAsJsonAsync(
            $"/api/matches/{match.Id}/court", new AssignCourtRequest(courtId, 1, null, null, null), Json);

        var responses = await Task.WhenAll(
            Enumerable.Range(2, 4).Select(sequence => client.PostAsJsonAsync(
                $"/api/matches/{match.Id}/court",
                new AssignCourtRequest(courtId, sequence, null, null, null),
                Json)));

        var assignmentIds = new List<Guid>();
        foreach (var response in responses.Where(r => r.IsSuccessStatusCode))
        {
            assignmentIds.Add((await response.Content.ReadFromJsonAsync<AssignCourtResult>(Json))!.AssignmentId);
        }

        Assert.Single(assignmentIds.Distinct());
        Assert.All(responses.Where(r => !r.IsSuccessStatusCode), r =>
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Ein_nachtraeglich_berufener_Turnierleiter_kann_einen_Platz_vergeben()
    {
        // Regression aus der Zeit des Vereins: die Plätze hingen an ihm, der
        // Filter kannte nur Vereinsrollen, und die Platzvergabe endete für den
        // Turnierleiter in einem 404 auf fremde Stammdaten. Der Fall ist
        // strukturell erledigt — die Plätze kommen jetzt mit dem Turnier —, und
        // der Test hält fest, dass das auch für einen Turnierleiter gilt, der
        // das Turnier nicht selbst angelegt hat.
        var (admin, courtId, tournamentId) = await DrawnTournamentAsync(4);
        var phase = Assert.Single(await PhasesAsync(admin, tournamentId));
        var match = phase.Matches.First(m => m.Round == 1);

        var director = $"director-{Guid.NewGuid():N}";
        await _factory.GrantAsync(director, Role.TournamentDirector, ResourceScope.Tournament(tournamentId));
        var client = _factory.CreateClientAs(director);

        var response = await client.PostAsJsonAsync(
            $"/api/matches/{match.Id}/court",
            new AssignCourtRequest(courtId, 1, null, null, null),
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Und der Platz trägt seinen Namen: fand ihn der Aufrufer nicht, blieb er „(unbekannt)".
        var updated = Assert.Single(await PhasesAsync(client, tournamentId));
        Assert.Equal("Platz 1", updated.Matches.First(m => m.Id == match.Id).Assignment!.CourtName);
    }

    [Fact]
    public async Task Ein_Freilos_laesst_sich_nicht_zurueckziehen()
    {
        // Regression: das Freilos ließ sich zurücknehmen wie ein eingetragenes
        // Ergebnis. Danach wartete die nächste Runde dauerhaft auf einen Sieger,
        // den niemand mehr eintragen kann — die Phase war nicht mehr spielbar.
        var (client, _, tournamentId) = await DrawnTournamentAsync(3);
        var phase = Assert.Single(await PhasesAsync(client, tournamentId));
        var bye = phase.Matches.Single(m => m.Score?.Outcome == MatchOutcome.Bye);

        var response = await client.DeleteAsync($"/api/matches/{bye.Id}/result");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var after = Assert.Single(await PhasesAsync(client, tournamentId));
        Assert.Equal(MatchOutcome.Bye, after.Matches.Single(m => m.Id == bye.Id).Score!.Outcome);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_darf_Ergebnisse_eintragen_aber_keine_Plaetze_vergeben()
    {
        var (admin, courtId, tournamentId) = await DrawnTournamentAsync(4);
        var phase = Assert.Single(await PhasesAsync(admin, tournamentId));
        var match = phase.Matches.First(m => m.Round == 1);

        var referee = $"referee-{Guid.NewGuid():N}";
        await _factory.GrantAsync(referee, Role.Referee, ResourceScope.Tournament(tournamentId));
        var client = _factory.CreateClientAs(referee);

        var result = await client.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);

        var assignment = await client.PostAsJsonAsync(
            $"/api/matches/{match.Id}/court",
            new AssignCourtRequest(courtId, 1, null, null, null),
            Json);
        Assert.Equal(HttpStatusCode.NotFound, assignment.StatusCode);
    }

    [Fact]
    public async Task Ein_unmoegliches_Satzergebnis_wird_abgewiesen()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync(4);
        var phase = Assert.Single(await PhasesAsync(client, tournamentId));
        var match = phase.Matches.First(m => m.Round == 1);

        var response = await client.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 5), new SetScore(6, 2)]),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>Spielt alle offenen Matches aus; Seite 1 gewinnt jeweils.</summary>
    [Fact]
    public async Task Das_letzte_Ergebnis_schliesst_das_Turnier_ab()
    {
        // Der Gegenzug zum ersten Ergebnis, das aus einem ausgelosten ein
        // laufendes Turnier macht. Ohne ihn stünde ein ausgespieltes Turnier
        // dauerhaft auf „läuft" — bis jemand daran denkt, „complete" zu
        // drücken, und niemand denkt daran.
        var (client, _, tournamentId) = await DrawnTournamentAsync(4);

        await PlayOutAsync(client, tournamentId);

        var detail = await client.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);

        Assert.Equal(TournamentState.Completed, detail!.State);
    }

    [Fact]
    public async Task Solange_noch_ein_Match_offen_ist_laeuft_das_Turnier()
    {
        // Die Gegenprobe: ohne sie wäre die Regel oben auch dann erfüllt, wenn
        // jedes Ergebnis das Turnier abschlösse.
        var (client, _, tournamentId) = await DrawnTournamentAsync(4);
        var phase = Assert.Single(await PhasesAsync(client, tournamentId));

        await client.PutAsJsonAsync(
            $"/api/matches/{phase.Matches.First(m => m.Round == 1).Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        var detail = await client.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);

        Assert.Equal(TournamentState.InProgress, detail!.State);
    }

    [Fact]
    public async Task Eine_Korrektur_des_Finales_nimmt_den_Abschluss_zurueck()
    {
        // Sonst wäre das Finale das einzige Match, dessen Ergebnis sich nicht
        // mehr korrigieren ließe: das Turnier wäre abgeschlossen, das Match
        // offen, und beides zugleich ginge nicht.
        var (client, _, tournamentId) = await DrawnTournamentAsync(4);
        await PlayOutAsync(client, tournamentId);

        var finale = FinalOf(Assert.Single(await PhasesAsync(client, tournamentId)));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/matches/{finale.Id}/result")).StatusCode);

        var offen = await client.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(TournamentState.InProgress, offen!.State);

        // Und der Weg wieder vorwärts: das neue Ergebnis schließt erneut ab.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync(
                $"/api/matches/{finale.Id}/result",
                new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 0), new SetScore(6, 0)]),
                Json)).StatusCode);

        var wiederZu = await client.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(TournamentState.Completed, wiederZu!.State);
    }

    private static async Task PlayOutAsync(HttpClient client, Guid tournamentId)
    {
        for (var guard = 0; guard < 64; guard++)
        {
            var phases = await PhasesAsync(client, tournamentId);
            var next = phases
                .SelectMany(p => p.Matches)
                .FirstOrDefault(m => m.Status == MatchStatus.Ready);

            if (next is null)
            {
                return;
            }

            var response = await client.PutAsJsonAsync(
                $"/api/matches/{next.Id}/result",
                new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
                Json);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Fail("Das Turnier ließ sich nicht zu Ende spielen.");
    }
}
