using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die Oberfläche (ADR-0009).
///
/// Geprüft wird das, was die Seiten zusagen und was ein Browser-Durchlauf nicht
/// dauerhaft festhalten kann: dass der Aushang ohne Anmeldung erreichbar ist, die
/// Verwaltung nicht, und dass im Bracket keine Ids stehen, wo ein Name hingehört.
/// </summary>
public sealed class WebUiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Eine Id in der Anzeige ist der Fehler, den diese Tests fernhalten.</summary>
    private static readonly Regex IdPattern = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    private readonly TennisTurnierApiFactory _factory;

    public WebUiTests(TennisTurnierApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Die_Live_Ansicht_ist_ohne_Anmeldung_erreichbar()
    {
        var (_, _, tournamentId) = await DrawnTournamentAsync();

        var response = await _factory.CreateClient().GetAsync($"/live/{tournamentId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Clubmeisterschaft", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Die_Live_Ansicht_zeigt_die_Herkunft_im_Klartext()
    {
        var (_, _, tournamentId) = await DrawnTournamentAsync();

        var html = await _factory.CreateClient().GetStringAsync($"/live/{tournamentId}");
        var bracket = BracketPart(html);

        // Bei acht Teilnehmern steht das Halbfinale schon im Baum, ohne dass
        // jemand darin feststeht — genau dort muss ein lesbarer Name stehen.
        Assert.Contains("Sieger aus Viertelfinale", bracket, StringComparison.Ordinal);
        Assert.DoesNotMatch(IdPattern, bracket);
    }

    [Fact]
    public async Task Die_Turnierseite_verlangt_eine_Anmeldung()
    {
        var (_, _, tournamentId) = await DrawnTournamentAsync();

        var response = await _factory.CreateClient().GetAsync($"/Tournaments/{tournamentId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ein_fremdes_Turnier_endet_als_nicht_gefunden()
    {
        var (_, _, tournamentId) = await DrawnTournamentAsync();

        // Angemeldet, aber ohne Rolle in diesem Turnier: wie in der API ein 404
        // und kein 403 — ein 403 bestätigte die Existenz (ADR-0004).
        await _factory.GrantAsync("ui-fremder", Role.SystemAdmin, ResourceScope.Global);
        var stranger = _factory.CreateClientAs("ui-niemand");

        var response = await stranger.GetAsync($"/Tournaments/{tournamentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Die_Meldeliste_nennt_die_Teilnehmer()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync();

        var html = await client.GetStringAsync($"/Tournaments/{tournamentId}");

        Assert.Contains("Meldungen", html, StringComparison.Ordinal);
        Assert.Contains("Im Feld", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Das_Bracket_nennt_die_Herkunft_im_Klartext()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync();

        var html = await client.GetStringAsync($"/Tournaments/{tournamentId}/draw");
        var bracket = BracketPart(html);

        Assert.Contains("Sieger aus Viertelfinale", bracket, StringComparison.Ordinal);
        Assert.DoesNotMatch(IdPattern, bracket);
    }

    [Fact]
    public async Task Ein_unsinniges_Satzergebnis_wird_als_Meldung_abgewiesen()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync();

        var phases = await client.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json);
        var match = phases!.SelectMany(p => p.Matches).First(m => m.Side1.EntryId is not null);

        var request = await FormAsync(client, $"/Tournaments/{tournamentId}/draw", new Dictionary<string, string>
        {
            ["tournamentId"] = tournamentId.ToString(),
            ["matchId"] = match.Id.ToString(),
            ["outcome"] = "Normal",
            ["sets"] = "quatsch",
        });

        var response = await client.SendAsync(request(HttpMethod.Post, "?handler=Result"));

        // 422 und ein lesbarer Rumpf: htmx tauscht dann nichts in die Seite ein
        // und hebt die Meldung in die Meldungszeile.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("ist kein Satz", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_Handlung_ohne_Antiforgery_Token_wird_abgewiesen()
    {
        var (client, _, tournamentId) = await DrawnTournamentAsync();

        var response = await client.PostAsync(
            $"/Tournaments/{tournamentId}?handler=Transition",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["tournamentId"] = tournamentId.ToString(),
                ["transition"] = "Start",
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Das_Stylesheet_und_htmx_werden_ausgeliefert()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/css/app.css")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/js/htmx.min.js")).StatusCode);
    }

    /// <summary>
    /// Baut eine Anfrage, wie htmx sie schickt: mit dem Antiforgery-Token als
    /// Header.
    ///
    /// Er steht im Layout am <c>body</c> und wird von dort an alle Formulare
    /// vererbt — dieselbe Stelle, aus der ihn ein Browser nimmt.
    /// </summary>
    private static async Task<Func<HttpMethod, string, HttpRequestMessage>> FormAsync(
        HttpClient client,
        string page,
        IDictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(page);
        var token = TokenPattern.Match(html);

        Assert.True(token.Success, $"Kein Antiforgery-Token auf {page}.");

        return (method, query) => new HttpRequestMessage(method, page + query)
        {
            Content = new FormUrlEncodedContent(fields),
            Headers = { { "RequestVerificationToken", token.Groups[1].Value } },
        };
    }

    private static readonly Regex TokenPattern = new(
        "RequestVerificationToken\":\\s*\"([^\"]+)\"",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Der Teil der Seite, in dem Namen stehen — ohne die Formulare.
    ///
    /// Ids in <c>value</c>-Attributen sind richtig und unvermeidlich; die Prüfung
    /// auf Klartext gilt dem, was ein Leser sieht.
    /// </summary>
    private static string BracketPart(string html) =>
        string.Concat(html
            .Split('<')
            .Select(fragment => fragment.Split('>') is [_, var text, ..] ? text : string.Empty));

    /// <summary>Ein ausgelostes Achtelfeld — genug für Viertel-, Halbfinale und Finale.</summary>
    private async Task<(HttpClient Client, Guid ClubId, Guid TournamentId)> DrawnTournamentAsync()
    {
        await _factory.GrantAsync("ui-admin", Role.SystemAdmin, ResourceScope.Global);
        var client = _factory.CreateClientAs("ui-admin");

        var clubId = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/api/clubs",
            new CreateClubRequest($"TC UI {Guid.NewGuid():N}", "Europe/Vienna", "Musterstadt"),
            Json));

        await client.PostAsJsonAsync(
            $"/api/clubs/{clubId}/courts",
            new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor),
            Json);

        var templates = await client.GetFromJsonAsync<List<FormatTemplateSummary>>(
            $"/api/clubs/{clubId}/format-templates", Json);
        var templateId = templates!.Single(t => t.Name == BuiltInFormats.Knockout.Name).Id;

        var tournamentId = await CreatedIdAsync(await client.PostAsJsonAsync(
            $"/api/clubs/{clubId}/tournaments",
            new CreateTournamentRequest(
                "Clubmeisterschaft", new DateOnly(2026, 5, 16), new DateOnly(2026, 5, 17), templateId),
            Json));

        await client.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null);

        for (var i = 1; i <= 8; i++)
        {
            var playerId = await CreatedIdAsync(await client.PostAsJsonAsync(
                "/api/players",
                new CreatePlayerRequest($"Vorname{i:00}", $"N{Guid.NewGuid():N}"[..10], null, null, null),
                Json));

            var participant = await (await client.PostAsJsonAsync(
                "/api/participants", new CreateParticipantRequest(playerId, null), Json))
                .Content.ReadFromJsonAsync<ParticipantSummary>(Json);

            var entryId = await CreatedIdAsync(await client.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/entries",
                new EnterTournamentRequest(participant!.Id, null),
                Json));

            await client.PostAsync($"/api/tournaments/{tournamentId}/entries/{entryId}/accept", null);
        }

        await client.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);
        await client.PostAsync($"/api/tournaments/{tournamentId}/draw", null);

        return (client, clubId, tournamentId);
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetGuid();
    }
}
