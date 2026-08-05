using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Turnier-Endpunkte aus den Rollen heraus, die sie im Betrieb benutzen.
///
/// Die übrigen Turniertests liefen einmal als <see cref="Role.SystemAdmin"/> —
/// und der kurzschließt sowohl den Query-Filter als auch jede Rechteprüfung.
/// Dadurch blieb unsichtbar, dass ein Turnierleiter sein eigenes Turnier gar
/// nicht erreichte. Diese Tests gehen deshalb bewusst durch die echten Rollen;
/// seit dem Selbstservice ist das ohnehin der Normalweg.
/// </summary>
public sealed class TournamentRoleTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public TournamentRoleTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>
    /// Ein Turnier im Entwurf samt dem Client seines Anlegers — mehr braucht
    /// keine Frage nach einer Rolle. Der Anleger ist Turnierleiter, ohne dass
    /// ihm jemand etwas zuweisen musste.
    /// </summary>
    private async Task<(HttpClient Leitung, Guid TournamentId)> SeedTournamentAsync()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Anlage = "TC Rollen", Auslosen = false });

        return (aufbau.Admin, aufbau.TournamentId);
    }

    [Fact]
    public async Task Wer_ein_Turnier_anlegt_fuehrt_es()
    {
        // Der Kern des Selbstservices: keine Freischaltung, keine Rollenvergabe
        // durch jemand anderen. Wäre die Zuweisung beim Anlegen verlorengegangen,
        // sähe der Anleger sein eigenes Turnier im nächsten Augenblick nicht mehr
        // — und ohne Rolle gäbe es keinen Weg zurück.
        var (leitung, tournamentId) = await SeedTournamentAsync();

        Assert.Equal(HttpStatusCode.OK, (await leitung.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await leitung.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null)).StatusCode);

        var detail = await leitung.GetFromJsonAsync<TournamentDetail>($"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(TournamentState.RegistrationOpen, detail!.State);
    }

    [Fact]
    public async Task Ein_nachtraeglich_berufener_Turnierleiter_kann_sein_Turnier_fuehren()
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
    }

    [Fact]
    public async Task Ein_Turnierleiter_erreicht_ein_fremdes_Turnier_nicht()
    {
        var (leitung, ownTournament) = await SeedTournamentAsync();
        var (_, foreignTournament) = await SeedTournamentAsync();

        Assert.Equal(HttpStatusCode.OK, (await leitung.GetAsync($"/api/tournaments/{ownTournament}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await leitung.GetAsync($"/api/tournaments/{foreignTournament}")).StatusCode);
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
        var (leitung, ownTournament) = await SeedTournamentAsync();
        await SeedTournamentAsync();

        var mine = await leitung.GetFromJsonAsync<List<TournamentSummary>>("/api/tournaments", Json);

        Assert.Equal([ownTournament], mine!.Select(t => t.Id));
    }

    [Fact]
    public async Task Kontaktdaten_eines_unbeteiligten_Spielers_bleiben_verborgen()
    {
        // Der Angriff lautete einmal: irgendwo ViewInternals haben und den
        // eigenen Verein im Query-String angeben — damit ließen sich die
        // Kontaktdaten jedes beliebigen Spielers lesen. Der Verein ist aus dem
        // Pfad verschwunden, und das Turnier darin ist geprüft; der Spieler muss
        // dort trotzdem gemeldet sein, sonst hätte die Prüfung keinerlei Bezug
        // zu ihm (ADR-0008).
        var (leitung, tournamentId) = await SeedTournamentAsync();

        var playerId = await TurnierAufbau.CreatedIdAsync(await leitung.PostAsJsonAsync(
            "/api/players",
            new CreatePlayerRequest("Anna", $"Unbeteiligt{Guid.NewGuid():N}"[..16],
                "geheim@example.invalid", "+43 1 234567", new DateOnly(1990, 3, 14)),
            Json));

        var response = await leitung.GetAsync($"/api/tournaments/{tournamentId}/players/{playerId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(
            "geheim@example.invalid",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Kontaktdaten_eines_gemeldeten_Spielers_sind_der_Turnierleitung_zugaenglich()
    {
        // Die Gegenprobe: ohne sie wäre die Regel oben auch dann erfüllt, wenn
        // niemand mehr an Kontaktdaten käme.
        var (leitung, tournamentId) = await SeedTournamentAsync();

        var playerId = await TurnierAufbau.CreatedIdAsync(await leitung.PostAsJsonAsync(
            "/api/players",
            new CreatePlayerRequest("Eva", $"Gemeldet{Guid.NewGuid():N}"[..14],
                "eva@example.invalid", null, null),
            Json));

        var participant = await (await leitung.PostAsJsonAsync(
            "/api/participants", new CreateParticipantRequest(playerId, null), Json))
            .Content.ReadFromJsonAsync<ParticipantSummary>(Json);

        await leitung.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null);
        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/entries",
            new EnterTournamentRequest(participant!.Id, null),
            Json);

        var detail = await leitung
            .GetFromJsonAsync<PlayerDetail>($"/api/tournaments/{tournamentId}/players/{playerId}", Json);

        Assert.Equal("eva@example.invalid", detail!.Email);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_kommt_nicht_an_Kontaktdaten()
    {
        // ViewInternals hat nur die Turnierleitung. Ein Schiedsrichter trägt
        // Ergebnisse ein und braucht dafür Namen, keine Telefonnummern.
        var (leitung, tournamentId) = await SeedTournamentAsync();

        var playerId = await TurnierAufbau.CreatedIdAsync(await leitung.PostAsJsonAsync(
            "/api/players",
            new CreatePlayerRequest("Lisa", $"Gemeldet{Guid.NewGuid():N}"[..14],
                "lisa@example.invalid", null, null),
            Json));

        var participant = await (await leitung.PostAsJsonAsync(
            "/api/participants", new CreateParticipantRequest(playerId, null), Json))
            .Content.ReadFromJsonAsync<ParticipantSummary>(Json);

        await leitung.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null);
        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/entries",
            new EnterTournamentRequest(participant!.Id, null),
            Json);

        var referee = $"referee-{Guid.NewGuid():N}";
        await _factory.GrantAsync(referee, Role.Referee, ResourceScope.Tournament(tournamentId));

        var response = await _factory.CreateClientAs(referee)
            .GetAsync($"/api/tournaments/{tournamentId}/players/{playerId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(
            "lisa@example.invalid",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Zwei_Meldungen_mit_derselben_Setzposition_werden_sofort_abgewiesen()
    {
        // Regression: geprüft wurde nur beim nachträglichen Setzen. Der Konflikt
        // schlug damit erst beim Auslosen zu, also nach Meldeschluss.
        var (leitung, tournamentId) = await SeedTournamentAsync();
        await leitung.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null);

        async Task<HttpResponseMessage> EnterWithSeedOneAsync()
        {
            var playerId = await TurnierAufbau.CreatedIdAsync(await leitung.PostAsJsonAsync(
                "/api/players",
                new CreatePlayerRequest("Gesetzt", $"Nr{Guid.NewGuid():N}"[..10], null, null, null),
                Json));

            var participant = await (await leitung.PostAsJsonAsync(
                "/api/participants", new CreateParticipantRequest(playerId, null), Json))
                .Content.ReadFromJsonAsync<ParticipantSummary>(Json);

            return await leitung.PostAsJsonAsync(
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
        var (leitung, tournamentId) = await SeedTournamentAsync();

        await leitung.PostAsync($"/api/tournaments/{tournamentId}/abandon", null);

        var response = await leitung.PostAsync($"/api/tournaments/{tournamentId}/scheduling/planning", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
