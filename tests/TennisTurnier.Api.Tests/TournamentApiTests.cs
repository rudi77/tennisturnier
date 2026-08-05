using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

public sealed class TournamentApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public TournamentApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>
    /// Ein angemeldeter Benutzer — mehr braucht es nicht mehr. Der Selbstservice
    /// macht ihn zum Veranstalter, und was er anlegt, führt er als Turnierleiter.
    /// </summary>
    private HttpClient VeranstalterClient() => _factory.CreateClientAs($"veranstalter-{Guid.NewGuid():N}");

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateParticipantAsync(HttpClient client, string firstName, string lastName)
    {
        var playerId = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/api/players",
            new CreatePlayerRequest(firstName, lastName, $"{firstName}@example.invalid", null, null),
            Json));

        var response = await client.PostAsJsonAsync(
            "/api/participants",
            new CreateParticipantRequest(playerId, null),
            Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var participant = await response.Content.ReadFromJsonAsync<ParticipantSummary>(Json);
        return participant!.Id;
    }

    private static async Task<Guid> KnockoutTemplateIdAsync(HttpClient client)
    {
        var templates = await client.GetFromJsonAsync<List<FormatTemplateSummary>>(
            "/api/format-templates", Json);

        return templates!.Single(t => t.Name == BuiltInFormats.Knockout.Name).Id;
    }

    private static CreateTournamentRequest Anlegen(string name, Guid templateId) => new(
        name,
        "TC Maria Alm",
        null,
        "Maria Alm",
        "Europe/Vienna",
        Discipline.Singles,
        new DateOnly(2026, 5, 16),
        new DateOnly(2026, 5, 17),
        templateId);

    [Fact]
    public async Task Die_mitgelieferten_Formatvorlagen_stehen_jedem_offen()
    {
        var client = VeranstalterClient();

        var templates = await client.GetFromJsonAsync<List<FormatTemplateSummary>>(
            "/api/format-templates", Json);

        Assert.NotNull(templates);
        Assert.Equal(4, templates.Count);
        Assert.All(templates, t => Assert.True(t.IsBuiltIn));
        Assert.Contains(templates, t => t.Name == BuiltInFormats.GroupThenKnockout.Name);
    }

    [Fact]
    public async Task Ein_Turnier_durchlaeuft_seinen_Lebenszyklus_ueber_die_API()
    {
        var client = VeranstalterClient();
        var templateId = await KnockoutTemplateIdAsync(client);

        var tournamentId = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/api/tournaments",
            Anlegen("Clubmeisterschaft 2026", templateId),
            Json));

        await client.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null);

        foreach (var name in new[] { "Anna", "Eva", "Lisa", "Sarah" })
        {
            var participantId = await CreateParticipantAsync(client, name, $"Spielerin{Guid.NewGuid():N}"[..12]);
            var entryId = await CreatedIdAsync(await client.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/entries",
                new EnterTournamentRequest(participantId, null),
                Json));

            await client.PostAsync($"/api/tournaments/{tournamentId}/entries/{entryId}/accept", null);
        }

        await client.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/tournaments/{tournamentId}/draw", null)).StatusCode);

        var afterDraw = await client.GetFromJsonAsync<TournamentDetail>($"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(TournamentState.DrawGenerated, afterDraw!.State);
        Assert.NotNull(afterDraw.Format);
        Assert.Equal(BuiltInFormats.KnockoutId, afterDraw.Format.Definition.Id);
        Assert.Equal(4, afterDraw.Entries.Count);

        await client.PostAsync($"/api/tournaments/{tournamentId}/start", null);
        await client.PostAsync($"/api/tournaments/{tournamentId}/complete", null);

        var completed = await client.GetFromJsonAsync<TournamentDetail>($"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(TournamentState.Completed, completed!.State);
    }

    [Fact]
    public async Task Nach_der_Auslosung_wird_eine_Nachmeldung_abgewiesen()
    {
        var (client, tournamentId) = await DrawnTournamentAsync();
        var participantId = await CreateParticipantAsync(client, "Nach", "Zuegler");

        var response = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/entries",
            new EnterTournamentRequest(participantId, null),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Das_Zuruecknehmen_der_Auslosung_verwirft_das_eingefrorene_Format()
    {
        var (client, tournamentId) = await DrawnTournamentAsync();

        await client.PostAsync($"/api/tournaments/{tournamentId}/registration/reopen", null);

        var detail = await client.GetFromJsonAsync<TournamentDetail>($"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(TournamentState.RegistrationOpen, detail!.State);
        Assert.Null(detail.Format);

        // Und jetzt ist die Nachmeldung möglich.
        var participantId = await CreateParticipantAsync(client, "Nach", "Zuegler");
        var response = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/entries",
            new EnterTournamentRequest(participantId, null),
            Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Eine_Aenderung_an_der_Vorlage_beruehrt_ein_ausgelostes_Turnier_nicht()
    {
        // Der Kern von ADR-0001, an der Außenkante geprüft.
        var client = VeranstalterClient();
        var builtInId = await KnockoutTemplateIdAsync(client);

        var ownTemplateId = await CreatedIdAsync(await client.PostAsJsonAsync(
            $"/api/format-templates/{builtInId}/copy",
            new CopyFormatTemplateRequest("Hausformat"),
            Json));

        var tournamentId = await DrawTournamentAsync(client, ownTemplateId);

        var update = await client.PutAsJsonAsync(
            $"/api/format-templates/{ownTemplateId}",
            new SaveFormatTemplateRequest(BuiltInFormats.League with { Name = "Ganz anderes Format" }),
            Json);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var detail = await client.GetFromJsonAsync<TournamentDetail>($"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(BuiltInFormats.KnockoutId, detail!.Format!.Definition.Id);
        Assert.Equal(1, detail.Format.TemplateVersion);

        var template = await client.GetFromJsonAsync<FormatTemplateDetail>(
            $"/api/format-templates/{ownTemplateId}", Json);
        Assert.Equal(2, template!.Version);
    }

    [Fact]
    public async Task Eine_mitgelieferte_Vorlage_gehoert_keinem_Veranstalter()
    {
        // Sichtbar heißt nicht änderbar. Als „nicht gefunden" und nicht als
        // „nicht erlaubt": auf demselben Weg liegen die eigenen Vorlagen
        // fremder Benutzer, und ein 403 verriete, dass es sie gibt (ADR-0004).
        var client = VeranstalterClient();
        var builtInId = await KnockoutTemplateIdAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/format-templates/{builtInId}",
            new SaveFormatTemplateRequest(BuiltInFormats.League),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Eine_mitgelieferte_Vorlage_laesst_sich_auch_vom_Systemadministrator_nicht_aendern()
    {
        // Die Regel steht im Aggregat und nicht in der Berechtigung: eine
        // mitgelieferte Vorlage ist unveränderlich, weil laufende Turniere auf
        // ihre Ids zeigen. Für eine Abwandlung gibt es die Kopie.
        var subject = $"vorlagen-admin-{Guid.NewGuid():N}";
        await _factory.GrantAsync(subject, Role.SystemAdmin, ResourceScope.Global);
        var client = _factory.CreateClientAs(subject);

        var builtInId = await KnockoutTemplateIdAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/format-templates/{builtInId}",
            new SaveFormatTemplateRequest(BuiltInFormats.League),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Kopie", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_ungueltige_Formatdefinition_wird_abgewiesen()
    {
        var client = VeranstalterClient();

        var response = await client.PostAsJsonAsync(
            "/api/format-templates",
            new SaveFormatTemplateRequest(BuiltInFormats.Knockout with { Phases = [] }),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Ein_fremdes_Turnier_ist_nicht_sichtbar()
    {
        var admin = VeranstalterClient();
        var templateId = await KnockoutTemplateIdAsync(admin);
        var tournamentId = await CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/tournaments", Anlegen("Fremd", templateId), Json));

        // Ein Schiedsrichter irgendwo anders: eine Rolle, aber nicht an diesem
        // Turnier — und damit für den Query-Filter jemand, für den es das
        // Turnier nicht gibt.
        var outsider = $"outsider-{Guid.NewGuid():N}";
        var otherTournamentId = await CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/tournaments", Anlegen("Anderswo", templateId), Json));
        await _factory.GrantAsync(outsider, Role.Referee, ResourceScope.Tournament(otherTournamentId));

        var response = await _factory.CreateClientAs(outsider).GetAsync($"/api/tournaments/{tournamentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Die_Spielersuche_liefert_keine_Kontaktdaten()
    {
        // Spieler fallen nicht unter den Query-Filter (ADR-0008); der Schutz muss
        // hier entstehen. Spieler anlegen darf, wer irgendein Turnier verwaltet
        // — die Rolle Organizer allein genügt dafür nicht.
        var client = VeranstalterClient();
        await CreatedIdAsync(await client.PostAsJsonAsync(
            "/api/tournaments", Anlegen("Turnier zur Suche", await KnockoutTemplateIdAsync(client)), Json));

        var lastName = $"Suchbar{Guid.NewGuid():N}"[..12];
        await CreateParticipantAsync(client, "Anna", lastName);

        var response = await client.GetAsync($"/api/players?q={lastName}");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(lastName, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("example.invalid", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", raw, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(HttpClient Client, Guid TournamentId)> DrawnTournamentAsync()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"turnier-api-{Guid.NewGuid():N}",
            new TurnierWunsch
            {
                Anlage = "TC Turnier",
                Name = "Turnier",
                Teilnehmer = 2,
                Setzen = false,
            });

        return (aufbau.Admin, aufbau.TournamentId);
    }

    private async Task<Guid> DrawTournamentAsync(HttpClient client, Guid templateId)
    {
        var tournamentId = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/api/tournaments", Anlegen("Turnier", templateId), Json));

        await client.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null);

        for (var i = 0; i < 2; i++)
        {
            var participantId = await CreateParticipantAsync(client, "Spielerin", $"Nr{Guid.NewGuid():N}"[..10]);
            var entryId = await CreatedIdAsync(await client.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/entries",
                new EnterTournamentRequest(participantId, null),
                Json));

            await client.PostAsync($"/api/tournaments/{tournamentId}/entries/{entryId}/accept", null);
        }

        await client.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);
        await client.PostAsync($"/api/tournaments/{tournamentId}/draw", null);

        return tournamentId;
    }
}
