using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Privat als Voreinstellung, öffentlich als Schalter.
///
/// Bis hierher galt: wer die Kennung eines Turniers kennt, sieht seine
/// Zuschaueransicht. Das war die Voraussetzung dafür, einen Link in die
/// Vereinsgruppe zu stellen — und zugleich die Regel, dass jedes Turnier von
/// Anfang an im Netz stand. Mit ADR-0012 ist ein Turnier zuerst eine Gruppe;
/// der Aushang bleibt möglich, aber er ist eine Entscheidung.
/// </summary>
public sealed class SichtbarkeitApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public SichtbarkeitApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private Task<AufgebautesTurnier> TurnierAsync(bool oeffentlich = false) =>
        _factory.NeuesTurnierAsync(
            $"sicht-{Guid.NewGuid():N}"[..20],
            new TurnierWunsch
            {
                Anlage = "TC Sichtbarkeit",
                Teilnehmer = 4,
                Oeffentlich = oeffentlich,
            });

    /// <summary>Der Rumpf einer Fehlerantwort ohne die traceId.</summary>
    private static async Task<string> AuskunftAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        return string.Join(
            '|',
            body.EnumerateObject()
                .Where(feld => feld.NameEquals("traceId") is false)
                .Select(feld => $"{feld.Name}={feld.Value}"));
    }

    [Fact]
    public async Task Ein_frisches_Turnier_ist_privat()
    {
        var turnier = await TurnierAsync();

        var fremder = _factory.CreateClient();
        var response = await fremder.GetAsync($"/public/tournaments/{turnier.TournamentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Und die Antwort ist dieselbe wie bei einem Turnier, das es nicht
        // gibt. Ein 403 verriete, dass es existiert (ADR-0004).
        var erfunden = await fremder.GetAsync($"/public/tournaments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, erfunden.StatusCode);

        // Verglichen wird die Auskunft ohne die traceId: die ist je Anfrage
        // verschieden und sagt über die Ressource nichts.
        Assert.Equal(await AuskunftAsync(response), await AuskunftAsync(erfunden));
    }

    [Fact]
    public async Task Geoeffnet_sieht_es_jeder()
    {
        var turnier = await TurnierAsync();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await turnier.Admin.PutAsJsonAsync(
                $"/api/tournaments/{turnier.TournamentId}/visibility",
                new SetVisibilityRequest(IsPublic: true),
                Json)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await _factory.CreateClient()
                .GetAsync($"/public/tournaments/{turnier.TournamentId}")).StatusCode);
    }

    [Fact]
    public async Task Und_der_Weg_zurueck_steht_offen()
    {
        // Wer feststellt, dass die Teilnehmerliste doch nicht ins Netz gehört,
        // muss sie wieder einsammeln können.
        var turnier = await TurnierAsync(oeffentlich: true);

        await turnier.Admin.PutAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/visibility",
            new SetVisibilityRequest(IsPublic: false),
            Json);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _factory.CreateClient()
                .GetAsync($"/public/tournaments/{turnier.TournamentId}")).StatusCode);
    }

    [Fact]
    public async Task Die_Turnierleitung_sieht_ihre_Ansicht_auch_privat()
    {
        // Sonst gäbe es keine Vorschau darauf, was Fremde sähen — und die
        // Entscheidung, ob man öffnet, fiele blind.
        var turnier = await TurnierAsync();

        Assert.Equal(
            HttpStatusCode.OK,
            (await turnier.Admin.GetAsync($"/public/tournaments/{turnier.TournamentId}")).StatusCode);
    }

    [Fact]
    public async Task Ein_Mitglied_sieht_sie_auch_privat()
    {
        var turnier = await TurnierAsync();
        var email = $"mitglied.{Guid.NewGuid():N}"[..24] + "@example.invalid";
        var mitglied = _factory.CreateClientAs($"mitglied-{Guid.NewGuid():N}", email);

        // Vorher nicht — er gehört noch nicht dazu.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await mitglied.GetAsync($"/public/tournaments/{turnier.TournamentId}")).StatusCode);

        await mitglied.GetAsync("/api/me");
        await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/roles",
            new GrantRoleRequest(email, Role.Member),
            Json);

        Assert.Equal(
            HttpStatusCode.OK,
            (await mitglied.GetAsync($"/public/tournaments/{turnier.TournamentId}")).StatusCode);
    }

    [Fact]
    public async Task Der_Systemadministrator_sieht_jede_Ansicht()
    {
        // Er sieht ohnehin jedes Turnier — eine Ansicht, die ausgerechnet ihm
        // verschlossen bliebe, wäre eine Ausnahme ohne Begründung.
        const string adminMail = "sicht.admin@example.invalid";
        using var fabrik = new TennisTurnierApiFactory([adminMail]);

        var turnier = await fabrik.NeuesTurnierAsync(
            $"sicht-{Guid.NewGuid():N}"[..20],
            new TurnierWunsch { Anlage = "TC Sichtbarkeit", Teilnehmer = 4 });

        var admin = fabrik.CreateClientAs($"sysadmin-{Guid.NewGuid():N}", adminMail);

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.GetAsync($"/public/tournaments/{turnier.TournamentId}")).StatusCode);
    }

    [Fact]
    public async Task Ein_Fremder_mit_Konto_sieht_sie_nicht()
    {
        // Angemeldet ist noch nicht dabei. Wer zu einem anderen Turnier gehört,
        // gehört deshalb nicht zu diesem.
        var turnier = await TurnierAsync();
        await TurnierAsync();

        var fremder = _factory.CreateClientAs($"fremd-{Guid.NewGuid():N}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fremder.GetAsync($"/public/tournaments/{turnier.TournamentId}")).StatusCode);
    }

    [Fact]
    public async Task Zweimal_dasselbe_umschalten_aendert_nichts()
    {
        // Der zweite Klick auf denselben Schalter ist keine Änderung — und darf
        // deshalb auch keine neue Version des Turniers erzeugen.
        var turnier = await TurnierAsync(oeffentlich: true);

        var vorher = await turnier.Admin.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{turnier.TournamentId}", Json);

        await turnier.Admin.PutAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/visibility",
            new SetVisibilityRequest(IsPublic: true),
            Json);

        var nachher = await turnier.Admin.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{turnier.TournamentId}", Json);

        Assert.True(nachher!.IsPublic);
        Assert.Equal(vorher!.Version, nachher.Version);
    }

    [Fact]
    public async Task Der_Zustand_steht_in_der_Turnieransicht_und_in_der_Liste()
    {
        // Die Oberfläche muss ihn zeigen können, ohne ihn zu erraten.
        var turnier = await TurnierAsync(oeffentlich: true);

        var detail = await turnier.Admin.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{turnier.TournamentId}", Json);
        Assert.True(detail!.IsPublic);

        var liste = await turnier.Admin.GetFromJsonAsync<List<TournamentSummary>>(
            "/api/tournaments", Json);
        Assert.True(liste!.Single(t => t.Id == turnier.TournamentId).IsPublic);
    }

    [Fact]
    public async Task Umschalten_darf_nur_wer_das_Turnier_fuehrt()
    {
        var turnier = await TurnierAsync();
        var fremder = _factory.CreateClientAs($"fremd-{Guid.NewGuid():N}");

        var response = await fremder.PutAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/visibility",
            new SetVisibilityRequest(IsPublic: true),
            Json);

        // 404 und nicht 403: sonst verriete die Antwort, dass es das Turnier
        // gibt (ADR-0004).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
