using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Was es nicht gibt — und was ein Zwischenspeicher schon hat.
///
/// „Nicht gefunden" ist die häufigste Antwort einer API und die am seltensten
/// geprüfte. Sie muss aber genau dieselbe sein wie „darfst du nicht" (ADR-0004),
/// und sie darf nichts verraten: der Anmeldelink steht in der Adresszeile, und
/// eine Antwort, die zwischen „Token unbekannt" und „Meldung geschlossen"
/// unterscheidet, wäre ein Orakel dafür, welche Links es gibt.
/// </summary>
public sealed class NichtGefundenApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public NichtGefundenApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private Task<AufgebautesTurnier> TurnierAsync() =>
        _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 2, Auslosen = false });

    [Fact]
    public async Task Ein_Beitrittslink_ohne_Token_fuehrt_nirgendwohin()
    {
        var response = await _factory
            .CreateClientAs($"neugierig-{Guid.NewGuid():N}")
            .GetAsync("/api/join/%20%20");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Import_in_ein_Turnier_das_es_nicht_gibt()
    {
        var client = _factory.CreateClientAs($"leitung-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync(
            $"/api/tournaments/{Guid.NewGuid()}/entries/import",
            new ImportEntriesRequest("Anna;Berger"),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Spieler_den_es_nicht_gibt()
    {
        var turnier = await TurnierAsync();

        var kontakt = await turnier.Admin.GetAsync(
            $"/api/tournaments/{turnier.TournamentId}/players/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, kontakt.StatusCode);

        // Und ein Teilnehmer lässt sich nicht aus ihm bilden.
        var teilnehmer = await turnier.Admin.PostAsJsonAsync(
            "/api/participants",
            new CreateParticipantRequest(Guid.NewGuid(), null, null),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, teilnehmer.StatusCode);
    }

    [Fact]
    public async Task Eine_Formatvorlage_die_es_nicht_gibt()
    {
        var client = _factory.CreateClientAs($"veranstalter-{Guid.NewGuid():N}");

        var response = await client.GetAsync($"/api/format-templates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Eine_benannte_Phase_steht_mit_ihrem_Namen_in_der_Uebersicht()
    {
        // Ohne Namen steht dort die Formatart. Ein Veranstalter, der seine
        // Phasen benennt, will sie aber wiedererkennen.
        var client = _factory.CreateClientAs($"veranstalter-{Guid.NewGuid():N}");

        var definition = BuiltInFormats.Knockout with
        {
            Id = $"benannt-{Guid.NewGuid():N}",
            Name = "Mit benannter Phase",
            Phases = [BuiltInFormats.Knockout.Phases[0] with { Name = "Hauptfeld" }],
        };

        var angelegt = await client.PostAsJsonAsync(
            "/api/format-templates",
            new SaveFormatTemplateRequest(definition),
            Json);

        Assert.Equal(HttpStatusCode.Created, angelegt.StatusCode);

        var liste = await client.GetFromJsonAsync<List<FormatTemplateSummary>>("/api/format-templates", Json);
        var eigene = liste!.Single(v => v.Name == "Mit benannter Phase");

        Assert.Equal(["Hauptfeld"], eigene.Phases);
    }

    [Fact]
    public async Task Ein_Stern_trifft_jede_vorhandene_Ansicht()
    {
        // „If-None-Match: *" heißt „schick mir nur, wenn es das überhaupt gibt".
        // Ein Aushang im Vereinsheim fragt so, und er soll dabei nichts laden.
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 4 });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.IfNoneMatch.Add(EntityTagHeaderValue.Any);

        var response = await client.GetAsync($"/public/tournaments/{turnier.TournamentId}");

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_laesst_sich_ohne_Weiteres_entlassen()
    {
        // Die Sperre gilt der letzten Turnierleitung, nicht jeder Rolle. Wer sie
        // auf alle ausdehnte, bekäme ein Turnier, dessen Schiedsrichter bleiben
        // müssten.
        var turnier = await TurnierAsync();
        var mail = $"schiri-{Guid.NewGuid():N}@example.invalid";

        // Der Schiedsrichter muss sich einmal angemeldet haben.
        await _factory.CreateClientAs($"schiri-{Guid.NewGuid():N}", mail).GetAsync("/api/me");

        var berufen = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/roles",
            new Application.Security.GrantRoleRequest(mail, Role.Referee),
            Json);

        Assert.Equal(HttpStatusCode.Created, berufen.StatusCode);
        var angelegt = await berufen.Content.ReadFromJsonAsync<JsonElement>(Json);

        var entzogen = await turnier.Admin.DeleteAsync(
            $"/api/tournaments/{turnier.TournamentId}/roles/{angelegt.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, entzogen.StatusCode);
    }
}
