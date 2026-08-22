using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Application.Common;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Kennungen, die es nicht gibt — an jedem Endpunkt, der eine entgegennimmt.
///
/// Jede davon kommt im Betrieb vor: ein Lesezeichen auf ein gelöschtes Turnier,
/// ein zweiter Browserreiter, in dem der Draw schon neu gelost wurde, ein
/// Ergebnis, das zweimal abgeschickt wird. Was dabei herauskommt, muss eine
/// Absage sein und kein Serverfehler — sonst steht in der Oberfläche „etwas ist
/// schiefgegangen", wo „gibt es nicht mehr" die Auskunft wäre.
/// </summary>
public sealed class UnbekannteKennungenApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public UnbekannteKennungenApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private HttpClient Leitung() => _factory.CreateClientAs($"leitung-{Guid.NewGuid():N}");

    [Fact]
    public async Task Ein_Ergebnis_zu_einem_Match_das_es_nicht_gibt()
    {
        var client = Leitung();

        var eingetragen = await client.PutAsJsonAsync(
            $"/api/matches/{Guid.NewGuid()}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)], null, null),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, eingetragen.StatusCode);

        var genommen = await client.DeleteAsync($"/api/matches/{Guid.NewGuid()}/result");
        Assert.Equal(HttpStatusCode.NotFound, genommen.StatusCode);
    }

    [Fact]
    public async Task Ein_Platz_fuer_ein_Match_das_es_nicht_gibt()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 2, Plaetze = 1, Auslosen = false });

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/matches/{Guid.NewGuid()}/court",
            new AssignCourtRequest(
                turnier.CourtIds[0], 1, null, null, TimeSpan.FromMinutes(60), Pinned: false),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Eine_Zuweisung_die_es_nicht_gibt_laesst_sich_nicht_ausrufen()
    {
        var client = Leitung();

        foreach (var schritt in new[] { "call", "start", "finish", "suspend" })
        {
            var response = await client.PostAsync($"/api/assignments/{Guid.NewGuid()}/{schritt}", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // Fortsetzen und Zusagen tragen eine Angabe mit — sonst dieselbe Absage.
        var fortgesetzt = await client.PostAsJsonAsync(
            $"/api/assignments/{Guid.NewGuid()}/resume",
            new ResumeMatchRequest(null),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, fortgesetzt.StatusCode);

        var zugesagt = await client.PostAsJsonAsync(
            $"/api/assignments/{Guid.NewGuid()}/promise",
            new PromiseStartRequest(_factory.Clock.Now.AddHours(1)),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, zugesagt.StatusCode);
    }

    [Fact]
    public async Task Eine_Platzuebersicht_zu_einem_Turnier_das_es_nicht_gibt()
    {
        var response = await Leitung().GetAsync($"/api/tournaments/{Guid.NewGuid()}/courts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Spielplan_zu_einem_Turnier_das_es_nicht_gibt()
    {
        var response = await Leitung().PostAsync($"/api/tournaments/{Guid.NewGuid()}/schedule/proposal", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ohne_Auslosung_gibt_es_keinen_Spielplan()
    {
        // Ein Spielplan setzt Matches voraus, und die entstehen beim Auslosen.
        // Vorher gibt es nichts zu planen — und das gehört gesagt, nicht als
        // leerer Vorschlag geliefert.
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 2, Plaetze = 1, Platzzeiten = true, Auslosen = false });

        var response = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/schedule/proposal", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains(
            "setzt eine Auslosung voraus",
            problem.GetProperty("detail").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_Ansicht_zu_einem_Turnier_das_es_nicht_gibt_laesst_sich_nicht_bauen()
    {
        // Der Anwendungsfall selbst, nicht der Endpunkt davor: er wird auch aus
        // anderen Anwendungsfällen heraus gerufen, und dort gibt es keine
        // vorgelagerte Prüfung.
        using var scope = _factory.CreateMigratedScope();
        var ansicht = scope.ServiceProvider.GetRequiredService<IPublicViewService>();

        await Assert.ThrowsAsync<NotFoundException>(() => ansicht.RebuildAsync(Guid.NewGuid()));
    }
}
