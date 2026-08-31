using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Was der Turniertag abweist.
///
/// Aufgerufen wird nur, wer feststeht, auf einen freien Platz und nicht vor
/// einer Zusage (ADR-0002). Jede dieser Absagen verhindert eine Situation, die
/// auf der Anlage nicht mehr zu reparieren ist: ein ausgerufener Platzhalter,
/// zwei Matches auf einem Platz, ein Aufruf vor der zugesagten Zeit.
/// </summary>
public sealed class TurniertagAbsagenApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public TurniertagAbsagenApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private async Task<AufgebautesTurnier> TurniertagAsync() =>
        await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch
            {
                Teilnehmer = 4,
                Plaetze = 2,
                Platzzeiten = true,
                Spielplan = true,
                Turniertag = true,
            });

    private static async Task<IReadOnlyList<CourtBoard>> BoardAsync(AufgebautesTurnier turnier) =>
        (await turnier.Admin.GetFromJsonAsync<MatchDayBoard>(
            $"/api/tournaments/{turnier.TournamentId}/courts", Json))!.Courts;

    private static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return problem.GetProperty("detail").GetString() ?? string.Empty;
    }

    [Fact]
    public async Task Eine_Reihenfolge_ohne_Zuweisungen_ist_keine()
    {
        var turnier = await TurniertagAsync();

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/courts/{turnier.CourtIds[0]}/queue",
            new ReorderQueueRequest(null!),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("nennt keine Zuweisungen", await DetailAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_Platz_ohne_Warteschlange_ist_nicht_gefunden()
    {
        var turnier = await TurniertagAsync();

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/courts/{Guid.NewGuid()}/queue",
            new ReorderQueueRequest([]),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Platzhalter_wird_nicht_ausgerufen()
    {
        var turnier = await TurniertagAsync();

        // Das Finale steht im Plan, lange bevor seine Teilnehmer feststehen —
        // am Platz wird es trotzdem nicht ausgerufen.
        var board = await BoardAsync(turnier);
        var wartend = board
            .SelectMany(c => c.Queue)
            .FirstOrDefault(q => q.MatchStatus == MatchStatus.Pending);

        Assert.NotNull(wartend);

        var response = await turnier.Admin.PostAsync(
            $"/api/assignments/{wartend.AssignmentId}/call",
            content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("wartet auf sein Vorspiel", await DetailAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_entschiedenes_Match_wird_nicht_mehr_ausgerufen()
    {
        var turnier = await TurniertagAsync();

        var board = await BoardAsync(turnier);
        var spielbar = board
            .SelectMany(c => c.Queue)
            .First(q => q.MatchStatus == MatchStatus.Ready);

        // Ein Nichtantreten wird eingetragen, ohne dass jemand zum Platz geht —
        // die Zuweisung ist damit hinfällig.
        var ergebnis = await turnier.Admin.PutAsJsonAsync(
            $"/api/matches/{spielbar.MatchId}/result",
            new RecordResultRequest(
                MatchOutcome.Normal,
                [new SetScore(6, 4), new SetScore(6, 3)],
                null,
                null),
            Json);

        Assert.Equal(HttpStatusCode.NoContent, ergebnis.StatusCode);

        // Wird es danach doch wieder auf einen Platz gesetzt, bleibt der Aufruf
        // verwehrt: gespielt wird da nichts mehr.
        var neu = await turnier.Admin.PostAsJsonAsync(
            $"/api/matches/{spielbar.MatchId}/court",
            new AssignCourtRequest(
                turnier.CourtIds[0],
                SequenceOnCourt: 9,
                PlannedStart: null,
                EarliestStart: null,
                EstimatedDuration: TimeSpan.FromMinutes(60),
                Pinned: false),
            Json);

        Assert.Equal(HttpStatusCode.OK, neu.StatusCode);
        var zuweisung = await neu.Content.ReadFromJsonAsync<AssignCourtResult>(Json);

        var response = await turnier.Admin.PostAsync(
            $"/api/assignments/{zuweisung!.AssignmentId}/call",
            content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("bereits entschieden", await DetailAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Einer_beendeten_Zuweisung_laesst_sich_nichts_mehr_zusagen()
    {
        var turnier = await TurniertagAsync();

        var board = await BoardAsync(turnier);
        var spielbar = board
            .SelectMany(c => c.Queue)
            .First(q => q.MatchStatus == MatchStatus.Ready);

        foreach (var schritt in new[] { "call", "start", "finish" })
        {
            var zug = await turnier.Admin.PostAsync(
                $"/api/assignments/{spielbar.AssignmentId}/{schritt}",
                content: null);

            Assert.Equal(HttpStatusCode.NoContent, zug.StatusCode);
        }

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/assignments/{spielbar.AssignmentId}/promise",
            new PromiseStartRequest(_factory.Clock.Now.AddHours(2)),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("nichts mehr zusagen", await DetailAsync(response), StringComparison.Ordinal);
    }
}
