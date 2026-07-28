using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der Turniertag über die API — die Abnahmebedingung für M7.
///
/// Die harte Randbedingung beim Tennis ist, dass die Matchdauer unbekannt ist.
/// Ein starres Zeitraster kippt beim ersten langen Match; deshalb ist am
/// Turniertag die Reihenfolge auf dem Platz die Aussage, und die Zeiten dahinter
/// sind Schätzungen, die nachgezogen werden (ADR-0002).
/// </summary>
public sealed class MatchDayApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public MatchDayApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>Ein ausgelostes Turnier mit bestätigtem Spielplan im Turniertagbetrieb.</summary>
    private async Task<(HttpClient Admin, Guid ClubId, Guid TournamentId)> MatchDayAsync(
        int participants = 8,
        int courts = 2)
    {
        await _factory.GrantAsync("tag-admin", Role.SystemAdmin, ResourceScope.Global);
        var admin = _factory.CreateClientAs("tag-admin");

        var clubId = await CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/clubs",
            new CreateClubRequest($"TC Turniertag {Guid.NewGuid():N}", "Europe/Vienna", null),
            Json));

        for (var i = 1; i <= courts; i++)
        {
            var courtId = await CreatedIdAsync(await admin.PostAsJsonAsync(
                $"/api/clubs/{clubId}/courts",
                new CreateCourtRequest($"Platz {i}", CourtSurface.Clay, CourtLocation.Outdoor),
                Json));

            foreach (var day in new[] { DayOfWeek.Saturday, DayOfWeek.Sunday })
            {
                await admin.PostAsJsonAsync(
                    $"/api/clubs/{clubId}/courts/{courtId}/availability",
                    new CreateAvailabilityRequest(
                        day, new TimeOnly(8, 0), new TimeOnly(21, 0), new DateOnly(2026, 1, 1), null),
                    Json);
            }
        }

        var templates = await admin.GetFromJsonAsync<List<FormatTemplateSummary>>(
            $"/api/clubs/{clubId}/format-templates", Json);

        var tournamentId = await CreatedIdAsync(await admin.PostAsJsonAsync(
            $"/api/clubs/{clubId}/tournaments",
            new CreateTournamentRequest(
                "Clubmeisterschaft",
                new DateOnly(2026, 5, 16),
                new DateOnly(2026, 5, 17),
                templates!.Single(t => t.Name == BuiltInFormats.Knockout.Name).Id),
            Json));

        await admin.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null);

        for (var i = 1; i <= participants; i++)
        {
            var playerId = await CreatedIdAsync(await admin.PostAsJsonAsync(
                "/api/players",
                new CreatePlayerRequest($"Vorname{i:00}", $"N{Guid.NewGuid():N}"[..10], null, null, null),
                Json));

            var participant = await (await admin.PostAsJsonAsync(
                "/api/participants", new CreateParticipantRequest(playerId, null), Json))
                .Content.ReadFromJsonAsync<ParticipantSummary>(Json);

            var entryId = await CreatedIdAsync(await admin.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/entries",
                new EnterTournamentRequest(participant!.Id, i),
                Json));

            await admin.PostAsync($"/api/tournaments/{tournamentId}/entries/{entryId}/accept", null);
        }

        await admin.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);
        await admin.PostAsync($"/api/tournaments/{tournamentId}/draw", null);

        var proposal = (await (await admin.PostAsync(
            $"/api/tournaments/{tournamentId}/schedule/proposal", null))
            .Content.ReadFromJsonAsync<SchedulePlanResult>(Json))!;

        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            new ConfirmScheduleRequest([.. proposal.Assignments.Select(a => new ConfirmedAssignment(
                a.MatchId, a.CourtId, a.SequenceOnCourt, a.PlannedStart, a.EstimatedDuration))]),
            Json);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/match-day", null)).StatusCode);

        return (admin, clubId, tournamentId);
    }

    private static async Task<List<CourtBoard>> BoardAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<List<CourtBoard>>(
            $"/api/tournaments/{tournamentId}/courts", Json))!;

    [Fact]
    public async Task Die_Platzuebersicht_zeigt_Warteschlangen()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();

        var board = await BoardAsync(admin, tournamentId);

        Assert.Equal(2, board.Count);
        Assert.All(board, court => Assert.Null(court.Current));
        Assert.All(board, court => Assert.NotEmpty(court.Queue));

        // Lückenlos ab 1: die Nummer wird am Platz vorgelesen.
        Assert.All(board, court => Assert.Equal(
            Enumerable.Range(1, court.Queue.Count), court.Queue.Select(q => q.SequenceOnCourt)));
    }

    [Fact]
    public async Task Ein_Match_laesst_sich_aufrufen_starten_und_beenden()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var first = (await BoardAsync(admin, tournamentId))[0].Queue[0];

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/assignments/{first.AssignmentId}/call", null)).StatusCode);

        Assert.Equal(AssignmentStatus.Called, (await BoardAsync(admin, tournamentId))[0].Current!.Status);

        await admin.PostAsync($"/api/assignments/{first.AssignmentId}/start", null);

        var running = (await BoardAsync(admin, tournamentId))[0].Current!;
        Assert.Equal(AssignmentStatus.Running, running.Status);
        Assert.NotNull(running.ActualStart);

        await admin.PostAsync($"/api/assignments/{first.AssignmentId}/finish", null);

        var after = (await BoardAsync(admin, tournamentId))[0];
        Assert.Null(after.Current);
        Assert.DoesNotContain(after.Queue, q => q.AssignmentId == first.AssignmentId);
    }

    [Fact]
    public async Task Nach_einem_beendeten_Match_rueckt_die_Warteschlange_nach()
    {
        // Der Kern des Tagesbetriebs: die Schätzungen der Wartenden werden
        // nachgezogen, sobald tatsächlich etwas passiert. Ein Plan, der nach dem
        // ersten Match noch die Zeiten von gestern zeigt, ist Fiktion.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];
        var estimatedBefore = court.Queue[1].EstimatedStart;

        await admin.PostAsync($"/api/assignments/{court.Queue[0].AssignmentId}/start", null);
        await admin.PostAsync($"/api/assignments/{court.Queue[0].AssignmentId}/finish", null);

        var after = (await BoardAsync(admin, tournamentId))[0];

        Assert.Equal(1, after.Queue[0].SequenceOnCourt);
        Assert.NotEqual(estimatedBefore, after.Queue[0].EstimatedStart);
    }

    [Fact]
    public async Task Eine_Zusage_wird_beim_Nachziehen_nicht_unterlaufen()
    {
        // „Nicht vor 14 Uhr" ist das Einzige, worauf sich ein Spieler verlassen
        // kann. Die Schätzung darf darunter nicht rutschen, auch wenn der Platz
        // früher frei wird.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];
        var promised = new DateTimeOffset(2026, 5, 17, 14, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync(
                $"/api/assignments/{court.Queue[1].AssignmentId}/promise",
                new PromiseStartRequest(promised),
                Json)).StatusCode);

        await admin.PostAsync($"/api/assignments/{court.Queue[0].AssignmentId}/start", null);
        await admin.PostAsync($"/api/assignments/{court.Queue[0].AssignmentId}/finish", null);

        var waiting = (await BoardAsync(admin, tournamentId))[0]
            .Queue.Single(q => q.AssignmentId == court.Queue[1].AssignmentId);

        Assert.Equal(promised, waiting.EarliestStart);
        Assert.True(waiting.EstimatedStart >= promised);
    }

    [Fact]
    public async Task Die_Warteschlange_laesst_sich_umstellen()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];
        var reversed = court.Queue.Select(q => q.AssignmentId).Reverse().ToList();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/courts/{court.CourtId}/queue",
                new ReorderQueueRequest(reversed),
                Json)).StatusCode);

        var after = (await BoardAsync(admin, tournamentId))[0];

        Assert.Equal(reversed, after.Queue.Select(q => q.AssignmentId));
        Assert.Equal(Enumerable.Range(1, after.Queue.Count), after.Queue.Select(q => q.SequenceOnCourt));
    }

    [Fact]
    public async Task Eine_unvollstaendige_Reihenfolge_wird_abgewiesen()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];

        var response = await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/courts/{court.CourtId}/queue",
            new ReorderQueueRequest([court.Queue[0].AssignmentId]),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Regenguss_unterbricht_und_die_Partie_geht_woanders_weiter()
    {
        // Die Abnahmebedingung: ein Turniertag mit Unterbrechung läuft durch,
        // ohne dass der Plan als Ganzes ungültig wird. Die unterbrochene
        // Zuweisung bleibt als Historie stehen — erst beide zusammen erzählen,
        // was an diesem Tag passiert ist (ADR-0002).
        var (admin, _, tournamentId) = await MatchDayAsync();
        var board = await BoardAsync(admin, tournamentId);
        var running = board[0].Queue[0];
        var otherCourt = board[1].CourtId;

        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/start", null);
        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/suspend", null);

        // Der alte Platz ist frei — die unterbrochene Partie blockiert ihn nicht.
        var suspended = await BoardAsync(admin, tournamentId);
        Assert.Null(suspended[0].Current);
        Assert.DoesNotContain(suspended[0].Queue, q => q.AssignmentId == running.AssignmentId);

        var resumed = await admin.PostAsJsonAsync(
            $"/api/assignments/{running.AssignmentId}/resume",
            new ResumeMatchRequest(otherCourt),
            Json);

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);

        var after = await BoardAsync(admin, tournamentId);
        var current = after.Single(court => court.CourtId == otherCourt).Current;

        Assert.NotNull(current);
        Assert.Equal(running.MatchId, current.MatchId);
        Assert.Equal(AssignmentStatus.Running, current.Status);
        Assert.NotEqual(running.AssignmentId, current.AssignmentId);
    }

    [Fact]
    public async Task Eine_unterbrochene_Partie_geht_auch_auf_demselben_Platz_weiter()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var running = (await BoardAsync(admin, tournamentId))[0].Queue[0];

        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/start", null);
        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/suspend", null);

        var response = await admin.PostAsJsonAsync(
            $"/api/assignments/{running.AssignmentId}/resume", new ResumeMatchRequest(), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var current = (await BoardAsync(admin, tournamentId))[0].Current!;

        Assert.Equal(running.AssignmentId, current.AssignmentId);
        Assert.Equal(AssignmentStatus.Running, current.Status);
    }

    [Fact]
    public async Task Die_oeffentliche_Ansicht_zeigt_die_Fortsetzung_und_nicht_den_alten_Platz()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var board = await BoardAsync(admin, tournamentId);
        var running = board[0].Queue[0];
        var otherCourt = board[1].CourtId;

        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/start", null);
        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/suspend", null);
        await admin.PostAsJsonAsync(
            $"/api/assignments/{running.AssignmentId}/resume", new ResumeMatchRequest(otherCourt), Json);

        var view = await _factory.CreateClient().GetFromJsonAsync<JsonElement>(
            $"/public/tournaments/{tournamentId}", Json);

        var match = view.GetProperty("phases").EnumerateArray().Single()
            .GetProperty("matches").EnumerateArray()
            .Single(m => m.GetProperty("id").GetGuid() == running.MatchId);

        Assert.Equal(
            board[1].CourtName,
            match.GetProperty("courtName").GetString());

        // Und das Match steht genau einmal in den Warteschlangen.
        var queued = view.GetProperty("courts").EnumerateArray()
            .SelectMany(court => court.GetProperty("queue").EnumerateArray())
            .Count(slot => slot.GetProperty("matchId").GetGuid() == running.MatchId);

        Assert.Equal(1, queued);
    }

    [Fact]
    public async Task Im_Planungsmodus_wird_nicht_aufgerufen()
    {
        // Der Wechsel in den Turniertagbetrieb ist ein ausdrücklicher Schritt: er
        // ändert die Bedeutung jeder angezeigten Uhrzeit.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var first = (await BoardAsync(admin, tournamentId))[0].Queue[0];

        await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/planning", null);

        var response = await admin.PostAsync($"/api/assignments/{first.AssignmentId}/call", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_darf_aufrufen_aber_nicht_umstellen()
    {
        // Er steht am Platz und ruft auf; die Reihenfolge der Warteschlange ist
        // Sache der Turnierleitung.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];

        var referee = $"referee-{Guid.NewGuid():N}";
        await _factory.GrantAsync(referee, Role.Referee, ResourceScope.Tournament(tournamentId));
        var client = _factory.CreateClientAs(referee);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/assignments/{court.Queue[0].AssignmentId}/call", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/courts/{court.CourtId}/queue",
                new ReorderQueueRequest([.. court.Queue.Select(q => q.AssignmentId).Reverse()]),
                Json)).StatusCode);
    }

    [Fact]
    public async Task Ein_Turniertag_mit_Verzug_und_Regen_laeuft_durch()
    {
        // Die Abnahmebedingung aus dem Fahrplan, als Ablauf: alle Erstrunden
        // werden gespielt, eines davon nach einer Unterbrechung auf einem anderen
        // Platz, und danach steht der Spielplan weiterhin.
        var (admin, _, tournamentId) = await MatchDayAsync();

        var interrupted = true;

        while (true)
        {
            var board = await BoardAsync(admin, tournamentId);

            // Aufgerufen wird nur, wer feststeht. Ein Halbfinale, dessen Vorspiel
            // noch läuft, steht zwar im Plan, aber am Platz wird kein Platzhalter
            // ausgerufen.
            var next = board.FirstOrDefault(court =>
                court.Current is null && court.Queue.Any(q => q.MatchStatus == MatchStatus.Ready));

            if (next is null)
            {
                break;
            }

            var assignmentId = next.Queue.First(q => q.MatchStatus == MatchStatus.Ready).AssignmentId;
            await admin.PostAsync($"/api/assignments/{assignmentId}/call", null);
            await admin.PostAsync($"/api/assignments/{assignmentId}/start", null);

            if (interrupted)
            {
                // Der Regen: unterbrechen und auf demselben Platz fortsetzen.
                interrupted = false;
                await admin.PostAsync($"/api/assignments/{assignmentId}/suspend", null);
                await admin.PostAsJsonAsync(
                    $"/api/assignments/{assignmentId}/resume", new ResumeMatchRequest(), Json);
            }

            var current = (await BoardAsync(admin, tournamentId))
                .Single(court => court.CourtId == next.CourtId).Current!;

            await admin.PostAsync($"/api/assignments/{current.AssignmentId}/finish", null);

            // Das Ergebnis kommt getrennt — der Platz ist frei, sobald die
            // Spieler ihn verlassen.
            await admin.PutAsJsonAsync(
                $"/api/matches/{current.MatchId}/result",
                new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
                Json);
        }

        var finalBoard = await BoardAsync(admin, tournamentId);

        Assert.All(finalBoard, court => Assert.Null(court.Current));
        Assert.All(finalBoard, court => Assert.Empty(court.Queue));

        var phases = await admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json);

        Assert.All(phases!.Single().Matches, match => Assert.NotNull(match.Score));
    }
}
