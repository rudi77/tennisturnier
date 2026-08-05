using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der Selbstservice, an seiner gefährlichsten Stelle geprüft.
///
/// Risiko 1 des Umbaus: wird das Turnier gespeichert, die Rollenzuweisung aber
/// nicht, sieht der Anleger sein eigenes Turnier im nächsten Augenblick nicht
/// mehr — der Query-Filter kennt seit dem Wegfall des Vereins keinen zweiten
/// Weg dorthin. Ohne Rolle gäbe es auch keinen zurück: er könnte sich die Rolle
/// nicht selbst geben, weil er das Turnier nicht sieht, das er dafür nennen
/// müsste.
///
/// Deshalb entstehen Turnier und Zuweisung in einer Arbeitseinheit, über einen
/// eigenen Port ohne eigenes Speichern. Diese Tests halten das an der Außenkante
/// fest — dort, wo es sich zeigen würde.
/// </summary>
public sealed class SelbstverwaltungApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public SelbstverwaltungApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static CreateTournamentRequest Anlegen(Guid templateId) => new(
        "Selbst ausgeschrieben",
        "TC Selbstservice",
        null,
        "Maria Alm",
        "Europe/Vienna",
        Discipline.Singles,
        new DateOnly(2026, 5, 16),
        new DateOnly(2026, 5, 17),
        templateId);

    private static async Task<Guid> TemplateIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<FormatTemplateSummary>>("/api/format-templates", Json))!
        .Single(t => t.Name == BuiltInFormats.Knockout.Name).Id;

    [Fact]
    public async Task Ein_frisch_angemeldeter_Benutzer_kann_sofort_ausschreiben()
    {
        // Kein Eintrag in einer Konfigurationsdatei, keine Freischaltung durch
        // jemand anderen. Vorher endete hier jede frische Instanz: Rollen
        // vergibt, wer eine Rolle hat, und nach einer Migration hat niemand eine.
        var client = _factory.CreateClientAs($"selbst-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync(
            "/api/tournaments", Anlegen(await TemplateIdAsync(client)), Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Wer_anlegt_findet_sein_Turnier_im_naechsten_Aufruf_wieder()
    {
        // Der eigentliche Regressionstest. Ginge die Rollenzuweisung verloren,
        // wäre das Turnier angelegt und für seinen Anleger dennoch verschwunden
        // — ein 404 auf etwas, das er gerade selbst erzeugt hat.
        var client = _factory.CreateClientAs($"selbst-{Guid.NewGuid():N}");

        var id = await TurnierAufbau.CreatedIdAsync(await client.PostAsJsonAsync(
            "/api/tournaments", Anlegen(await TemplateIdAsync(client)), Json));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tournaments/{id}")).StatusCode);

        var meine = await client.GetFromJsonAsync<List<TournamentSummary>>("/api/tournaments", Json);
        Assert.Contains(meine!, t => t.Id == id);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);
        Assert.Contains(me!.Roles, r => r.Role == Role.TournamentDirector && r.ResourceId == id);
    }

    [Fact]
    public async Task Er_darf_es_auch_sofort_fuehren()
    {
        // Sichtbarkeit allein genügte nicht: die Rolle muss auch die
        // Berechtigung tragen, sonst sähe er sein Turnier und käme keinen
        // Schritt weiter.
        var client = _factory.CreateClientAs($"selbst-{Guid.NewGuid():N}");

        var id = await TurnierAufbau.CreatedIdAsync(await client.PostAsJsonAsync(
            "/api/tournaments", Anlegen(await TemplateIdAsync(client)), Json));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/tournaments/{id}/registration/open", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Created,
            (await client.PostAsJsonAsync(
                $"/api/tournaments/{id}/courts",
                new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor),
                Json)).StatusCode);
    }

    [Fact]
    public async Task Ein_zweiter_Veranstalter_sieht_davon_nichts()
    {
        // Die Rolle Organizer ist global und trotzdem harmlos: ihr einziges
        // Recht ist das Anlegen. Wäre stattdessen ManageTournament global
        // vergeben, hätte jeder Veranstalter jedes fremde Turnier in der Hand.
        var erster = _factory.CreateClientAs($"selbst-{Guid.NewGuid():N}");
        var id = await TurnierAufbau.CreatedIdAsync(await erster.PostAsJsonAsync(
            "/api/tournaments", Anlegen(await TemplateIdAsync(erster)), Json));

        var zweiter = _factory.CreateClientAs($"selbst-{Guid.NewGuid():N}");

        // Er darf ausschreiben …
        Assert.Equal(
            HttpStatusCode.Created,
            (await zweiter.PostAsJsonAsync(
                "/api/tournaments", Anlegen(await TemplateIdAsync(zweiter)), Json)).StatusCode);

        // … und sieht das fremde Turnier trotzdem nicht.
        Assert.Equal(HttpStatusCode.NotFound, (await zweiter.GetAsync($"/api/tournaments/{id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await zweiter.PostAsync($"/api/tournaments/{id}/registration/open", null)).StatusCode);
        Assert.DoesNotContain(
            (await zweiter.GetFromJsonAsync<List<TournamentSummary>>("/api/tournaments", Json))!,
            t => t.Id == id);
    }

    [Fact]
    public async Task Eine_abgewiesene_Anlage_hinterlaesst_keine_Rollenzuweisung()
    {
        // Die andere Richtung derselben Klammer: scheitert das Anlegen, darf
        // auch keine Zuweisung übrig bleiben. Eine Rolle an einem Turnier, das
        // es nicht gibt, wäre eine Zeile, die niemand je wieder anfasst.
        var subject = $"selbst-{Guid.NewGuid():N}";
        var client = _factory.CreateClientAs(subject);

        var response = await client.PostAsJsonAsync(
            "/api/tournaments", Anlegen(Guid.NewGuid()), Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);
        Assert.DoesNotContain(me!.Roles, r => r.Role == Role.TournamentDirector);
    }
}
