using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Das Doppel über die API.
///
/// Ein Teilnehmer ist die spielende Einheit — im Einzel ein Spieler, im Doppel
/// zwei. Der Teamname ist die einzige Zutat, die nicht aus den Spielern folgt,
/// und er ersetzt sie nicht: aus dem Spielplan muss hervorgehen, wer auf dem
/// Platz steht.
/// </summary>
public sealed class DoublesApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public DoublesApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private async Task<HttpClient> AdminClientAsync()
    {
        await _factory.GrantAsync("doubles-admin", Role.SystemAdmin, ResourceScope.Global);
        return _factory.CreateClientAs("doubles-admin");
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreatePlayerAsync(HttpClient client, string firstName, string lastName) =>
        await CreatedIdAsync(await client.PostAsJsonAsync(
            "/api/players",
            new CreatePlayerRequest(firstName, lastName, null, null, null),
            Json));

    private static async Task<HttpResponseMessage> CreateParticipantAsync(
        HttpClient client,
        Guid first,
        Guid? second,
        string? teamName) =>
        await client.PostAsJsonAsync(
            "/api/participants",
            new CreateParticipantRequest(first, second, teamName),
            Json);

    [Fact]
    public async Task Ein_Doppel_ohne_Teamnamen_heisst_nach_seinen_Spielern()
    {
        var client = await AdminClientAsync();
        var anna = await CreatePlayerAsync(client, "Anna", "Müller");
        var eva = await CreatePlayerAsync(client, "Eva", "Berger");

        var response = await CreateParticipantAsync(client, anna, eva, teamName: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var participant = await response.Content.ReadFromJsonAsync<ParticipantSummary>(Json);

        Assert.Equal("Müller, Anna / Berger, Eva", participant!.DisplayName);
        Assert.Equal(2, participant.PlayerIds.Count);
    }

    [Fact]
    public async Task Der_Teamname_steht_den_Spielernamen_voran_statt_sie_zu_ersetzen()
    {
        // Der Punkt der ganzen Übung: „Die Netzroller" ist die Antwort auf
        // „gegen wen spielen wir", die Spielernamen sind die Antwort auf „wer
        // steht auf Platz 3". Der Spielplan braucht beide.
        var client = await AdminClientAsync();
        var anna = await CreatePlayerAsync(client, "Anna", "Müller");
        var eva = await CreatePlayerAsync(client, "Eva", "Berger");

        var response = await CreateParticipantAsync(client, anna, eva, "Die Netzroller");
        var participant = await response.Content.ReadFromJsonAsync<ParticipantSummary>(Json);

        Assert.Equal("Die Netzroller · Müller, Anna / Berger, Eva", participant!.DisplayName);
    }

    [Fact]
    public async Task Leerraum_als_Teamname_gilt_als_keiner()
    {
        var client = await AdminClientAsync();
        var anna = await CreatePlayerAsync(client, "Anna", "Müller");
        var eva = await CreatePlayerAsync(client, "Eva", "Berger");

        var response = await CreateParticipantAsync(client, anna, eva, "   ");
        var participant = await response.Content.ReadFromJsonAsync<ParticipantSummary>(Json);

        Assert.Equal("Müller, Anna / Berger, Eva", participant!.DisplayName);
    }

    [Fact]
    public async Task Ein_Teamname_ohne_zweiten_Spieler_wird_abgewiesen()
    {
        // Nicht stillschweigend verwerfen: wer einen Teamnamen schickt, meinte
        // ein Doppel und hat den Partner vergessen.
        var client = await AdminClientAsync();
        var anna = await CreatePlayerAsync(client, "Anna", "Müller");

        var response = await CreateParticipantAsync(client, anna, second: null, "Die Netzroller");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Doppel_mit_demselben_Spieler_zweimal_wird_abgewiesen()
    {
        var client = await AdminClientAsync();
        var anna = await CreatePlayerAsync(client, "Anna", "Müller");

        var response = await CreateParticipantAsync(client, anna, anna, teamName: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Ein_ueberlanger_Teamname_wird_abgewiesen()
    {
        var client = await AdminClientAsync();
        var anna = await CreatePlayerAsync(client, "Anna", "Müller");
        var eva = await CreatePlayerAsync(client, "Eva", "Berger");

        var response = await CreateParticipantAsync(client, anna, eva, new string('x', 200));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Doppelturnier_laesst_sich_auslosen()
    {
        // Die eigentliche Frage: trägt das Doppel bis zum Draw durch? Die
        // Formatvorlage kennt keine Disziplin — ein Doppelturnier ist eines,
        // dessen Teilnehmer Paare sind.
        var client = await AdminClientAsync();

        var clubId = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/api/clubs",
            new CreateClubRequest($"TC Doppel {Guid.NewGuid():N}", "Europe/Vienna", null),
            Json));

        var templates = await client.GetFromJsonAsync<List<FormatTemplateSummary>>(
            $"/api/clubs/{clubId}/format-templates", Json);
        var templateId = templates!.First(t => t.Phases.Count == 1).Id;

        var tournamentId = await CreatedIdAsync(await client.PostAsJsonAsync(
            $"/api/clubs/{clubId}/tournaments",
            new CreateTournamentRequest(
                "Doppel-Clubmeisterschaft",
                new DateOnly(2026, 5, 16),
                new DateOnly(2026, 5, 17),
                templateId),
            Json));

        await client.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null);

        foreach (var team in new[] { "Netzroller", "Grundlinie", "Volleyfreunde", "Rückhand" })
        {
            var first = await CreatePlayerAsync(client, "Anna", $"A{Guid.NewGuid():N}"[..10]);
            var second = await CreatePlayerAsync(client, "Eva", $"B{Guid.NewGuid():N}"[..10]);

            var participant = await (await CreateParticipantAsync(client, first, second, team))
                .Content.ReadFromJsonAsync<ParticipantSummary>(Json);

            var entryId = await CreatedIdAsync(await client.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/entries",
                new EnterTournamentRequest(participant!.Id, null),
                Json));

            await client.PostAsync($"/api/tournaments/{tournamentId}/entries/{entryId}/accept", null);
        }

        await client.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);
        var draw = await client.PostAsync($"/api/tournaments/{tournamentId}/draw", null);

        Assert.Equal(HttpStatusCode.NoContent, draw.StatusCode);

        var tournament = await client.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);

        Assert.All(
            tournament!.Entries,
            entry => Assert.Contains(" · ", entry.ParticipantName, StringComparison.Ordinal));
    }
}
