using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Rollen an einem Turnier vergeben und entziehen.
///
/// Der Punkt, an dem eine frische Instanz lange stehenblieb: Rollen vergibt, wer
/// eine Rolle hat, und einen Endpunkt dafür gab es nicht. Zwei Sperren tragen
/// den Anwendungsfall, und beide sind hier festgehalten — die Eskalationssperre
/// und das herrenlose Turnier.
/// </summary>
public sealed class RollenApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public RollenApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>Ein Turnier samt dem Client seines Anlegers — er ist Turnierleiter.</summary>
    private async Task<(HttpClient Leitung, Guid TournamentId)> TurnierAsync()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"rollen-leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Anlage = "TC Rollenvergabe", Auslosen = false });

        return (aufbau.Admin, aufbau.TournamentId);
    }

    /// <summary>
    /// Ein Konto, das es schon gibt. Berufen lässt sich nur, wer sich einmal
    /// angemeldet hat — die Einladung eines Unbekannten ist ein offener Punkt.
    /// </summary>
    private async Task<(HttpClient Client, string Email)> AngemeldeterBenutzerAsync(string rolle)
    {
        var email = $"{rolle}.{Guid.NewGuid():N}"[..24] + "@example.invalid";
        var client = _factory.CreateClientAs($"{rolle}-{Guid.NewGuid():N}", email);

        // Der erste Aufruf legt das Konto an — vorher kennt es niemand.
        await client.GetAsync("/api/me");

        return (client, email);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_laesst_sich_berufen_und_traegt_dann_Ergebnisse_ein()
    {
        var (leitung, tournamentId) = await TurnierAsync();
        var (referee, email) = await AngemeldeterBenutzerAsync("referee");

        // Vorher sieht er das Turnier nicht.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await referee.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        var response = await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Referee),
            Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Und danach schon — die Rolle wirkt beim nächsten Request.
        Assert.Equal(
            HttpStatusCode.OK,
            (await referee.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        // Führen darf er es trotzdem nicht.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await referee.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null)).StatusCode);
    }

    [Fact]
    public async Task Die_Rollenliste_nennt_den_Anleger_als_Turnierleiter()
    {
        var (leitung, tournamentId) = await TurnierAsync();

        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        var eintrag = Assert.Single(rollen!);
        Assert.Equal(Role.TournamentDirector, eintrag.Role);
    }

    [Fact]
    public async Task Dieselbe_Rolle_zweimal_zu_vergeben_aendert_nichts()
    {
        // Der zweite Klick auf dieselbe Schaltfläche ist keine Änderung.
        var (leitung, tournamentId) = await TurnierAsync();
        var (_, email) = await AngemeldeterBenutzerAsync("referee");

        var request = new GrantRoleRequest(email, Role.Referee);

        await leitung.PostAsJsonAsync($"/api/tournaments/{tournamentId}/roles", request, Json);
        await leitung.PostAsJsonAsync($"/api/tournaments/{tournamentId}/roles", request, Json);

        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        Assert.Single(rollen!, r => r.Role == Role.Referee);
    }

    [Fact]
    public async Task Ein_Mitglied_sieht_sein_Turnier_und_aendert_nichts_daran()
    {
        // Die Rolle, die ein Turnier zur Gruppe macht. Sie ist der ganze
        // Unterschied zwischen „kennt die Adresse" und „gehoert dazu" — und
        // sie gewaehrt trotzdem kein einziges Recht.
        var (leitung, tournamentId) = await TurnierAsync();
        var (mitglied, email) = await AngemeldeterBenutzerAsync("mitglied");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await mitglied.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        var response = await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Member),
            Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Es sieht das Turnier — und findet es unter seinen eigenen.
        Assert.Equal(
            HttpStatusCode.OK,
            (await mitglied.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        var meine = await mitglied.GetFromJsonAsync<List<TournamentSummary>>(
            "/api/tournaments", Json);
        Assert.Contains(meine!, t => t.Id == tournamentId);

        // Aendern darf es nichts: die Meldung zu oeffnen ist Sache der Leitung.
        var versuch = await mitglied.PostAsync(
            $"/api/tournaments/{tournamentId}/registration/open", null);
        Assert.Equal(HttpStatusCode.NotFound, versuch.StatusCode);
    }

    [Theory]
    [InlineData(Role.SystemAdmin)]
    [InlineData(Role.Organizer)]
    public async Task Ein_Turnierleiter_darf_keine_globale_Rolle_vergeben(Role global)
    {
        // Die Eskalationssperre. Ohne sie machte sich ein Turnierleiter über ein
        // zweites Konto, das ihm gehört, zum Systemadministrator — und das ist
        // kein theoretischer Weg, sondern ein einziger Aufruf.
        var (leitung, tournamentId) = await TurnierAsync();
        var (_, email) = await AngemeldeterBenutzerAsync("kandidat");

        var response = await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, global),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        Assert.DoesNotContain(rollen!, r => r.Role == global);
    }

    [Fact]
    public async Task Die_letzte_Turnierleitung_laesst_sich_nicht_entziehen()
    {
        // Sonst entstünde ein herrenloses Turnier: der Query-Filter kennt keinen
        // zweiten Weg dorthin, und ohne Sicht darauf ließe sich auch keine
        // Rolle mehr daran vergeben. Eine Einbahnstraße, kein Ärgernis.
        var (leitung, tournamentId) = await TurnierAsync();

        var eigene = (await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json))!.Single();

        var response = await leitung.DeleteAsync(
            $"/api/tournaments/{tournamentId}/roles/{eigene.AssignmentId}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("letzte", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(
            HttpStatusCode.OK,
            (await leitung.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);
    }

    [Fact]
    public async Task Mit_einer_zweiten_Turnierleitung_laesst_sich_die_erste_entziehen()
    {
        // Die Gegenprobe: ohne sie wäre die Regel oben auch dann erfüllt, wenn
        // sich überhaupt keine Turnierleitung entziehen ließe — und der
        // Übergabefall ist der Normalfall.
        var (leitung, tournamentId) = await TurnierAsync();
        var (nachfolger, email) = await AngemeldeterBenutzerAsync("nachfolge");

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.TournamentDirector),
            Json);

        var eigene = (await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json))!
            .First(r => r.Email != email && r.Role == Role.TournamentDirector);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await leitung.DeleteAsync(
                $"/api/tournaments/{tournamentId}/roles/{eigene.AssignmentId}")).StatusCode);

        // Der Nachfolger führt es weiter …
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await nachfolger.PostAsync(
                $"/api/tournaments/{tournamentId}/registration/open", null)).StatusCode);

        // … und der Vorgänger sieht es nicht mehr.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await leitung.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_darf_keine_Rollen_vergeben()
    {
        var (leitung, tournamentId) = await TurnierAsync();
        var (referee, refereeEmail) = await AngemeldeterBenutzerAsync("referee");
        var (_, kandidat) = await AngemeldeterBenutzerAsync("kandidat");

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(refereeEmail, Role.Referee),
            Json);

        // Er sieht das Turnier — und darf trotzdem nicht darüber verfügen.
        Assert.Equal(
            HttpStatusCode.OK,
            (await referee.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await referee.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/roles",
                new GrantRoleRequest(kandidat, Role.Referee),
                Json)).StatusCode);
    }

    [Fact]
    public async Task Ein_Aussenstehender_sieht_die_Rollenliste_nicht()
    {
        // Als 404 und nicht als 403: ein 403 bestätigte, dass es dieses Turnier
        // gibt (ADR-0004).
        var (_, tournamentId) = await TurnierAsync();
        var fremder = _factory.CreateClientAs($"fremder-{Guid.NewGuid():N}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fremder.GetAsync($"/api/tournaments/{tournamentId}/roles")).StatusCode);
    }

    [Fact]
    public async Task Wer_noch_nie_angemeldet_war_laesst_sich_nicht_berufen()
    {
        // Die Grenze, die ADR-0007 zieht: Identitäten legt der Identity
        // Provider an, nicht diese Anwendung. Die Einladung eines noch nicht
        // angemeldeten Benutzers bleibt ein benannter offener Punkt — und der
        // Fehler sagt genau das, statt still nichts zu tun.
        var (leitung, tournamentId) = await TurnierAsync();

        var response = await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest("niemand@example.invalid", Role.Referee),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(
            "angemeldet",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }
}
