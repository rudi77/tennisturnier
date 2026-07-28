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
/// Der Spielplan über die API — die Abnahmebedingung für M6.
///
/// Zwei Schritte, und das ist der Kern: rechnen ändert nichts, erst das
/// Bestätigen wirkt. Ein Solverlauf, der den Plan still überschreibt, ist genau
/// das, was Turnierleitungen dazu bringt, die Automatik abzuschalten (ADR-0002).
/// </summary>
public sealed class SchedulingApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public SchedulingApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<(HttpClient Admin, Guid ClubId, Guid TournamentId)> DrawnAsync(
        int participants = 16,
        int courts = 4)
    {
        await _factory.GrantAsync("plan-admin", Role.SystemAdmin, ResourceScope.Global);
        var admin = _factory.CreateClientAs("plan-admin");

        var clubId = await CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/clubs",
            new CreateClubRequest($"TC Spielplan {Guid.NewGuid():N}", "Europe/Vienna", null),
            Json));

        for (var i = 1; i <= courts; i++)
        {
            var courtId = await CreatedIdAsync(await admin.PostAsJsonAsync(
                $"/api/clubs/{clubId}/courts",
                new CreateCourtRequest($"Platz {i}", CourtSurface.Clay, CourtLocation.Outdoor, IsCenterCourt: i == 1),
                Json));

            // Ohne Öffnungszeiten hat kein Platz ein freies Fenster, und der
            // Solver hätte nichts, worin er planen könnte.
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
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/tournaments/{tournamentId}/draw", null)).StatusCode);

        return (admin, clubId, tournamentId);
    }

    private static async Task<SchedulePlanResult> ProposeAsync(HttpClient client, Guid tournamentId)
    {
        var response = await client.PostAsync($"/api/tournaments/{tournamentId}/schedule/proposal", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SchedulePlanResult>(Json))!;
    }

    private static ConfirmScheduleRequest From(SchedulePlanResult proposal) =>
        new([.. proposal.Assignments.Select(assignment => new ConfirmedAssignment(
            assignment.MatchId,
            assignment.CourtId,
            assignment.SequenceOnCourt,
            assignment.PlannedStart,
            assignment.EstimatedDuration))]);

    private static async Task<List<PhaseDetail>> PhasesAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json))!;

    [Fact]
    public async Task Ein_Vorschlag_setzt_jedes_Match_an_und_begruendet_es()
    {
        var (admin, _, tournamentId) = await DrawnAsync();

        var proposal = await ProposeAsync(admin, tournamentId);
        var matches = (await PhasesAsync(admin, tournamentId)).Single().Matches;

        Assert.Equal(matches.Count, proposal.Assignments.Count);
        Assert.Empty(proposal.Unscheduled);
        Assert.Empty(proposal.Violations);
        Assert.All(proposal.Assignments, a => Assert.False(string.IsNullOrWhiteSpace(a.Reason)));
        Assert.Equal(matches.Count, proposal.Diff.Added);
    }

    [Fact]
    public async Task Ein_Vorschlag_allein_veraendert_nichts()
    {
        // Der Kern der Trennung: Rechnen ist folgenlos.
        var (admin, _, tournamentId) = await DrawnAsync();

        await ProposeAsync(admin, tournamentId);

        var matches = (await PhasesAsync(admin, tournamentId)).Single().Matches;

        Assert.All(matches, match => Assert.Null(match.Assignment));
    }

    [Fact]
    public async Task Erst_die_Bestaetigung_traegt_den_Plan_ein()
    {
        var (admin, _, tournamentId) = await DrawnAsync();
        var proposal = await ProposeAsync(admin, tournamentId);

        var response = await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm", From(proposal), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var matches = (await PhasesAsync(admin, tournamentId)).Single().Matches;

        Assert.All(matches, match => Assert.NotNull(match.Assignment));
        Assert.All(matches, match => Assert.Equal(AssignmentSource.Auto, match.Assignment!.Source));
    }

    [Fact]
    public async Task Ein_bestaetigter_Plan_steht_auch_in_der_oeffentlichen_Ansicht()
    {
        var (admin, _, tournamentId) = await DrawnAsync();
        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            From(await ProposeAsync(admin, tournamentId)),
            Json);

        var view = await _factory.CreateClient().GetFromJsonAsync<JsonElement>(
            $"/public/tournaments/{tournamentId}", Json);

        var queued = view.GetProperty("courts").EnumerateArray()
            .Sum(court => court.GetProperty("queue").GetArrayLength());

        Assert.Equal((await PhasesAsync(admin, tournamentId)).Single().Matches.Count, queued);
    }

    [Fact]
    public async Task Ein_zweiter_Lauf_nach_der_Bestaetigung_schlaegt_nichts_Neues_vor()
    {
        // Ohne diese Eigenschaft bekäme die Turnierleitung bei jedem Klick einen
        // anderen Aushang.
        var (admin, _, tournamentId) = await DrawnAsync();
        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            From(await ProposeAsync(admin, tournamentId)),
            Json);

        var again = await ProposeAsync(admin, tournamentId);

        Assert.Equal(0, again.Diff.Moved);
        Assert.Equal(0, again.Diff.Added);
        Assert.All(again.Assignments, a => Assert.Equal(ProposalChange.Unchanged, a.Change));
    }

    [Fact]
    public async Task Eine_Verschiebung_von_Hand_ueberlebt_den_naechsten_Lauf()
    {
        var (admin, clubId, tournamentId) = await DrawnAsync();
        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            From(await ProposeAsync(admin, tournamentId)),
            Json);

        var match = (await PhasesAsync(admin, tournamentId)).Single().Matches.First(m => m.Round == 1);
        var courts = await admin.GetFromJsonAsync<List<CourtDetail>>($"/api/clubs/{clubId}/courts", Json);
        var moved = new DateTimeOffset(2026, 5, 17, 15, 0, 0, TimeSpan.FromHours(2));

        await admin.PostAsJsonAsync(
            $"/api/matches/{match.Id}/court",
            new AssignCourtRequest(courts![^1].Id, 1, moved, null, TimeSpan.FromMinutes(75)),
            Json);

        var again = await ProposeAsync(admin, tournamentId);
        var kept = again.Assignments.Single(a => a.MatchId == match.Id);

        Assert.Equal(moved, kept.PlannedStart);
        Assert.Equal(courts[^1].Id, kept.CourtId);
        Assert.Equal(ProposalChange.Unchanged, kept.Change);
        Assert.Contains("Hand", kept.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_Oeffnungszeiten_sagt_der_Vorschlag_warum_nichts_geht()
    {
        var (admin, _, tournamentId) = await DrawnAsync(participants: 4, courts: 0);

        var proposal = await ProposeAsync(admin, tournamentId);

        Assert.Empty(proposal.Assignments);
        Assert.NotEmpty(proposal.Unscheduled);
        Assert.All(proposal.Unscheduled, un => Assert.False(string.IsNullOrWhiteSpace(un.Reason)));
    }

    [Fact]
    public async Task Im_Turniertagbetrieb_wird_nicht_mehr_geplant()
    {
        // Dort ist eine Startzeit eine Behauptung; es zählt die Reihenfolge auf
        // dem Platz (ADR-0002).
        var (admin, _, tournamentId) = await DrawnAsync();
        var proposal = await ProposeAsync(admin, tournamentId);

        await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/match-day", null);

        var response = await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm", From(proposal), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_darf_keinen_Plan_rechnen_lassen()
    {
        var (_, _, tournamentId) = await DrawnAsync(participants: 4);

        var referee = $"referee-{Guid.NewGuid():N}";
        await _factory.GrantAsync(referee, Role.Referee, ResourceScope.Tournament(tournamentId));

        var response = await _factory.CreateClientAs(referee)
            .PostAsync($"/api/tournaments/{tournamentId}/schedule/proposal", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ein_bereits_gespieltes_Match_wird_nicht_mehr_eingeplant()
    {
        // Zeit für etwas zu reservieren, das schon gespielt ist, wäre ein Platz,
        // der den ganzen Tag leer bleibt.
        var (admin, _, tournamentId) = await DrawnAsync();
        var match = (await PhasesAsync(admin, tournamentId)).Single()
            .Matches.First(m => m.Status == MatchStatus.Ready);

        await admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        var proposal = await ProposeAsync(admin, tournamentId);

        Assert.DoesNotContain(proposal.Assignments, a => a.MatchId == match.Id);
        Assert.DoesNotContain(proposal.Unscheduled, u => u.MatchId == match.Id);
    }
}
