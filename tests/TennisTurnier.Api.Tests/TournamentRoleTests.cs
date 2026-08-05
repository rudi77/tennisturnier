using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Turnier-Endpunkte aus den Rollen heraus, die sie im Betrieb benutzen.
///
/// Die übrigen Turniertests laufen als <see cref="Role.SystemAdmin"/> — und der
/// kurzschließt sowohl den Query-Filter als auch jede Rechteprüfung. Dadurch
/// blieb unsichtbar, dass ein Turnierleiter sein eigenes Turnier gar nicht
/// erreichte. Diese Tests gehen deshalb bewusst durch die echten Rollen.
/// </summary>
public sealed class TournamentRoleTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public TournamentRoleTests(TennisTurnierApiFactory factory) => _factory = factory;

    private async Task<HttpClient> AdminClientAsync()
    {
        await _factory.GrantAsync("role-tests-admin", Role.SystemAdmin, ResourceScope.Global);
        return _factory.CreateClientAs("role-tests-admin");
    }

    /// <summary>Ein Turnier im Entwurf — mehr braucht keine Frage nach einer Rolle.</summary>
    private async Task<(Guid ClubId, Guid TournamentId)> SeedTournamentAsync()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            "role-tests-admin",
            new TurnierWunsch { Verein = "TC Rollen", Auslosen = false });

        return (aufbau.ClubId, aufbau.TournamentId);
    }

    [Fact]
    public async Task Ein_Turnierleiter_ohne_Vereinsrolle_kann_sein_Turnier_fuehren()
    {
        // Regression: der Query-Filter kannte nur Vereinsrollen, also lief jeder
        // Aufruf eines reinen Turnierleiters in ein 404 — auch das Auslosen,
        // wofür er ausdrücklich berufen wurde.
        var (_, tournamentId) = await SeedTournamentAsync();

        var director = $"director-{Guid.NewGuid():N}";
        await _factory.GrantAsync(director, Role.TournamentDirector, ResourceScope.Tournament(tournamentId));
        var client = _factory.CreateClientAs(director);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null)).StatusCode);

        var detail = await client.GetFromJsonAsync<TournamentDetail>($"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(TournamentState.RegistrationOpen, detail!.State);
    }

    [Fact]
    public async Task Ein_Turnierleiter_erreicht_ein_fremdes_Turnier_nicht()
    {
        var (_, ownTournament) = await SeedTournamentAsync();
        var (_, foreignTournament) = await SeedTournamentAsync();

        var director = $"director-{Guid.NewGuid():N}";
        await _factory.GrantAsync(director, Role.TournamentDirector, ResourceScope.Tournament(ownTournament));
        var client = _factory.CreateClientAs(director);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tournaments/{ownTournament}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/tournaments/{foreignTournament}")).StatusCode);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_darf_das_Turnier_sehen_aber_nicht_fuehren()
    {
        var (_, tournamentId) = await SeedTournamentAsync();

        var referee = $"referee-{Guid.NewGuid():N}";
        await _factory.GrantAsync(referee, Role.Referee, ResourceScope.Tournament(tournamentId));
        var client = _factory.CreateClientAs(referee);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null)).StatusCode);
    }

    [Fact]
    public async Task Ein_angemeldeter_Benutzer_ohne_Rolle_sieht_das_Turnier_nicht()
    {
        // Die neue Grenze: es gibt keinen Weg zu einem Turnier, der nicht über
        // eine Rolle an genau diesem Turnier führt. Vorher genügte eine Rolle im
        // ausrichtenden Verein — auch die eines bloßen Vereinsmitglieds.
        var (_, tournamentId) = await SeedTournamentAsync();

        var stranger = _factory.CreateClientAs($"fremder-{Guid.NewGuid():N}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);
    }

    [Fact]
    public async Task Die_eigene_Turnierliste_enthaelt_nur_Turniere_mit_Rolle()
    {
        var (_, ownTournament) = await SeedTournamentAsync();
        await SeedTournamentAsync();

        var director = $"director-{Guid.NewGuid():N}";
        await _factory.GrantAsync(director, Role.TournamentDirector, ResourceScope.Tournament(ownTournament));

        var mine = await _factory.CreateClientAs(director)
            .GetFromJsonAsync<List<TournamentSummary>>("/api/tournaments", Json);

        Assert.Equal([ownTournament], mine!.Select(t => t.Id));
    }

    [Fact]
    public async Task Kontaktdaten_eines_unbeteiligten_Spielers_bleiben_verborgen()
    {
        // Regression: der Verein kam ungeprüft aus dem Query-String. Wer irgendwo
        // ViewInternals hatte, konnte damit die Kontaktdaten jedes beliebigen
        // Spielers lesen, indem er seinen eigenen Verein angab. Die Berechtigung
        // allein genügt deshalb nicht — geprüft wird hier mit dem Aufrufer, der
        // sie sicher hat.
        var admin = await AdminClientAsync();
        var (ownClubId, _) = await SeedTournamentAsync();

        var playerId = await TurnierAufbau.CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/players",
            new CreatePlayerRequest("Anna", $"Unbeteiligt{Guid.NewGuid():N}"[..16],
                "geheim@example.invalid", "+43 1 234567", new DateOnly(1990, 3, 14)),
            Json));

        var response = await admin.GetAsync($"/api/players/{playerId}?clubId={ownClubId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(
            "geheim@example.invalid",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Kontaktdaten_eines_gemeldeten_Spielers_sind_im_ausrichtenden_Verein_sichtbar()
    {
        // Die Gegenprobe: ohne sie wäre die Regel oben auch dann erfüllt, wenn
        // niemand mehr an Kontaktdaten käme.
        var admin = await AdminClientAsync();
        var (clubId, tournamentId) = await SeedTournamentAsync();

        var playerId = await TurnierAufbau.CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/players",
            new CreatePlayerRequest("Eva", $"Gemeldet{Guid.NewGuid():N}"[..14],
                "eva@example.invalid", null, null),
            Json));

        var participant = await (await admin.PostAsJsonAsync(
            "/api/participants", new CreateParticipantRequest(playerId, null), Json))
            .Content.ReadFromJsonAsync<ParticipantSummary>(Json);

        await admin.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null);
        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/entries",
            new EnterTournamentRequest(participant!.Id, null),
            Json);

        var detail = await admin
            .GetFromJsonAsync<PlayerDetail>($"/api/players/{playerId}?clubId={clubId}", Json);

        Assert.Equal("eva@example.invalid", detail!.Email);
    }

    [Fact]
    public async Task Zwei_Meldungen_mit_derselben_Setzposition_werden_sofort_abgewiesen()
    {
        // Regression: geprüft wurde nur beim nachträglichen Setzen. Der Konflikt
        // schlug damit erst beim Auslosen zu, also nach Meldeschluss.
        var admin = await AdminClientAsync();
        var (_, tournamentId) = await SeedTournamentAsync();
        await admin.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null);

        async Task<HttpResponseMessage> EnterWithSeedOneAsync()
        {
            var playerId = await TurnierAufbau.CreatedIdAsync(await admin.PostAsJsonAsync(
                "/api/players",
                new CreatePlayerRequest("Gesetzt", $"Nr{Guid.NewGuid():N}"[..10], null, null, null),
                Json));

            var participant = await (await admin.PostAsJsonAsync(
                "/api/participants", new CreateParticipantRequest(playerId, null), Json))
                .Content.ReadFromJsonAsync<ParticipantSummary>(Json);

            return await admin.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/entries",
                new EnterTournamentRequest(participant!.Id, Seed: 1),
                Json);
        }

        Assert.Equal(HttpStatusCode.Created, (await EnterWithSeedOneAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await EnterWithSeedOneAsync()).StatusCode);
    }

    [Fact]
    public async Task Ein_abgebrochenes_Turnier_laesst_sich_nicht_mehr_umschalten()
    {
        // Regression: SwitchToPlanning war die einzige ändernde Methode ohne
        // Zustandsprüfung und änderte auch abgeschlossene Turniere noch.
        var admin = await AdminClientAsync();
        var (_, tournamentId) = await SeedTournamentAsync();

        await admin.PostAsync($"/api/tournaments/{tournamentId}/abandon", null);

        var response = await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/planning", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
