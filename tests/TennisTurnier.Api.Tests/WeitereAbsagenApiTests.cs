using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Absagen quer durch die Anwendungsschicht: eine Platzzeit, die rückwärts
/// läuft, ein Import, der eine ganze Mitgliederdatei enthält, eine Berufung
/// ohne Adresse, eine Suche ohne Suchwort.
///
/// Sie haben gemeinsam, dass sie nicht aus der Domäne kommen, sondern aus dem
/// Anwendungsfall — und dass sie deshalb nirgends sonst geprüft werden. Wer
/// eine davon versehentlich entfernt, merkt es erst, wenn ein Turnier Plätze
/// von 21 bis 8 Uhr hat.
/// </summary>
public sealed class WeitereAbsagenApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public WeitereAbsagenApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return problem.GetProperty("detail").GetString() ?? string.Empty;
    }

    [Fact]
    public async Task Ohne_Anmeldung_ist_niemand_angemeldet()
    {
        // Die Oberfläche fragt das vor dem ersten Klick. „Niemand" ist die
        // Antwort und kein Fehler — sonst zeigte die Startseite eine Störung.
        var response = await _factory.CreateClient().GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Eine_Suche_ohne_Suchwort_liefert_nichts()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 2, Auslosen = false });

        // Kein Suchwort heißt nicht „alle Spieler" — die Suche dient dem
        // Auffinden beim Melden, nicht dem Durchblättern der Datei.
        var treffer = await turnier.Admin.GetFromJsonAsync<List<PlayerSummary>>(
            "/api/players?q=%20%20", Json);

        Assert.Empty(treffer!);
    }

    [Fact]
    public async Task Wer_kein_Turnier_verwaltet_legt_keinen_Spieler_an()
    {
        // Spieler existieren turnierübergreifend (ADR-0008). Anlegen darf sie
        // trotzdem nur, wer irgendein Turnier führt — sonst wäre die Spielerdatei
        // für jeden Angemeldeten beschreibbar.
        var fremder = _factory.CreateClientAs($"fremder-{Guid.NewGuid():N}");

        var response = await fremder.PostAsJsonAsync(
            "/api/players",
            new CreatePlayerRequest("Anna", "Neu", null, null, null),
            Json);

        // Abgewiesen wird als „nicht gefunden": eine Antwort, die zwischen
        // „gibt es nicht" und „darfst du nicht" unterscheidet, verrät schon das
        // (ADR-0004).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var suche = await fremder.GetAsync("/api/players?q=Neu");
        Assert.Equal(HttpStatusCode.NotFound, suche.StatusCode);
    }

    [Fact]
    public async Task Eine_Platzzeit_laeuft_vorwaerts()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"platzwart-{Guid.NewGuid():N}",
            new TurnierWunsch { Plaetze = 1, Auslosen = false });

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/courts/windows",
            new CreateCourtWindowsRequest(new TimeOnly(21, 0), new TimeOnly(8, 0)),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("muss vorwärts laufen", await DetailAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_Platz_gibt_es_keine_Platzzeit()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"platzwart-{Guid.NewGuid():N}",
            new TurnierWunsch { Plaetze = 0, Auslosen = false });

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/courts/windows",
            new CreateCourtWindowsRequest(new TimeOnly(8, 0), new TimeOnly(21, 0)),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("keinen Platz", await DetailAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_ganze_Mitgliederdatei_ist_kein_Turnierfeld()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 2, Auslosen = false });

        var zeilen = new StringBuilder();
        for (var i = 1; i <= 513; i++)
        {
            zeilen.Append("Spielerin ").Append(i).Append(";Nachname\n");
        }

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/entries/import",
            new ImportEntriesRequest(zeilen.ToString()),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("513 Zeilen", await DetailAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eingeladen_wird_ueber_eine_Adresse()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Auslosen = false });

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/roles",
            new GrantRoleRequest("   ", Role.Referee),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("über eine E-Mail-Adresse", await DetailAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_unveraenderte_Ansicht_wird_nicht_neu_geschrieben()
    {
        // Zweimal veröffentlichen ohne Änderung dazwischen: die Projektion bleibt
        // dieselbe, und es entsteht kein zweiter Schreibvorgang (ADR-0003).
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 4 });

        var erste = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/public-view/rebuild",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, erste.StatusCode);

        var vorher = await turnier.Admin.GetAsync($"/api/public/{turnier.TournamentId}");
        var etag = vorher.Headers.ETag?.Tag;

        var zweite = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/public-view/rebuild",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, zweite.StatusCode);

        var nachher = await turnier.Admin.GetAsync($"/api/public/{turnier.TournamentId}");
        Assert.Equal(etag, nachher.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Eine_verlorene_Ansicht_wird_auf_Zuruf_neu_gebaut()
    {
        // Genau dafür gibt es den Endpunkt: die Projektion ist ein abgeleiteter
        // Stand (ADR-0003), und wenn er einmal fehlt, muss er sich ohne
        // Änderung am Turnier zurückholen lassen.
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 4, Oeffentlich = true });

        using (var scope = _factory.CreateMigratedScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<Adapters.Persistence.Sqlite.TennisTurnierDbContext>();

            var projektion = await db.TournamentProjections
                .FirstAsync(p => p.Id == turnier.TournamentId);

            db.TournamentProjections.Remove(projektion);
            await db.SaveChangesAsync();
        }

        // 204: sichtbar, aber ohne Ansicht — der Unterschied zu „nicht
        // vorhanden oder privat" (404) ist für den Zuschauer der ganze Punkt.
        var weg = await _factory.CreateClient().GetAsync($"/public/tournaments/{turnier.TournamentId}");
        Assert.Equal(HttpStatusCode.NoContent, weg.StatusCode);

        var neu = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/public-view/rebuild",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, neu.StatusCode);

        var wieder = await _factory.CreateClient().GetAsync($"/public/tournaments/{turnier.TournamentId}");
        Assert.Equal(HttpStatusCode.OK, wieder.StatusCode);
    }

    [Fact]
    public async Task Ein_geloeschtes_Turnier_nimmt_seine_Ansetzungen_mit()
    {
        // Ansetzungen hängen nicht am Turnier-Aggregat (ADR-0002) und würden
        // sonst als verwaiste Zeilen zurückbleiben.
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 4, Plaetze = 2, Platzzeiten = true, Spielplan = true });

        var board = (await turnier.Admin.GetFromJsonAsync<MatchDayBoard>(
            $"/api/tournaments/{turnier.TournamentId}/courts", Json))!.Courts;

        Assert.NotEmpty(board!.SelectMany(c => c.Queue));

        var geloescht = await turnier.Admin.DeleteAsync($"/api/tournaments/{turnier.TournamentId}");
        Assert.Equal(HttpStatusCode.NoContent, geloescht.StatusCode);

        using var scope = _factory.CreateMigratedScope();
        var db = scope.ServiceProvider
            .GetRequiredService<Adapters.Persistence.Sqlite.TennisTurnierDbContext>();

        Assert.Empty(await db.CourtAssignments
            .IgnoreQueryFilters()
            .Where(a => a.TournamentId == turnier.TournamentId)
            .ToListAsync());
    }
}
