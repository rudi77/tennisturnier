using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die Ränder von Planung und Auslosung, die im geraden Ablauf nicht vorkommen.
///
/// Ein Platz, der zwischen Vorschlag und Bestätigung stillgelegt wurde. Ein
/// Match, das in derselben Zeit gespielt wurde. Eine Vorlage, die jemand
/// anderem gehört. Alles drei entsteht daraus, dass zwei Menschen gleichzeitig
/// am selben Turnier arbeiten — und genau dann darf die Antwort keine 500 sein.
/// </summary>
public sealed class PlanungsrandfaelleApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public PlanungsrandfaelleApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        return body.GetProperty("id").GetGuid();
    }

    private static ConfirmScheduleRequest Uebernahme(SchedulePlanResult plan) =>
        new([.. plan.Assignments.Select(a => new ConfirmedAssignment(
            a.MatchId, a.CourtId, a.SequenceOnCourt, a.PlannedStart, a.EstimatedDuration))]);

    private async Task<(AufgebautesTurnier Turnier, SchedulePlanResult Plan)> VorschlagAsync(
        TurnierWunsch? wunsch = null)
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            wunsch ?? new TurnierWunsch { Teilnehmer = 4, Plaetze = 2, Platzzeiten = true });

        var response = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/schedule/proposal", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (turnier, (await response.Content.ReadFromJsonAsync<SchedulePlanResult>(Json))!);
    }

    [Fact]
    public async Task Ein_inzwischen_stillgelegter_Platz_nimmt_keine_Ansetzung_mehr()
    {
        var (turnier, plan) = await VorschlagAsync();
        var belegt = plan.Assignments[0].CourtId;

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await turnier.Admin.PutAsJsonAsync(
                $"/api/tournaments/{turnier.TournamentId}/courts/{belegt}",
                new UpdateCourtRequest("Platz 1", IsCenterCourt: false, IsActive: false),
                Json)).StatusCode);

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/schedule/confirm",
            Uebernahme(plan),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ein_inzwischen_gespieltes_Match_laesst_sich_nicht_mehr_ansetzen()
    {
        // Der Vorschlag ist überholt: neu rechnen, nicht suchen. Deshalb 409 und
        // nicht 404 — das Match gibt es ja, es ist nur nichts mehr zu planen.
        var (turnier, plan) = await VorschlagAsync();
        var gespielt = plan.Assignments[0].MatchId;

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await turnier.Admin.PutAsJsonAsync(
                $"/api/matches/{gespielt}/result",
                new RecordResultRequest(
                    MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)], null, null),
                Json)).StatusCode);

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/schedule/confirm",
            Uebernahme(plan),
            Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Ein_zweiphasiges_Turnier_bekommt_seinen_Spielplan()
    {
        // Nur mit zwei Phasen zeigt sich, dass das Endspiel die letzte Runde der
        // letzten Phase ist — und nicht jede letzte Runde einer Phase.
        var (turnier, plan) = await VorschlagAsync(new TurnierWunsch
        {
            Vorlage = BuiltInFormats.GroupThenKnockout.Name,
            Teilnehmer = 8,
            Plaetze = 2,
            Platzzeiten = true,
        });

        var bestaetigt = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/schedule/confirm",
            Uebernahme(plan),
            Json);

        Assert.Equal(HttpStatusCode.OK, bestaetigt.StatusCode);
        Assert.NotEmpty(plan.Assignments);
    }

    [Fact]
    public async Task Eine_fremde_Vorlage_taugt_nicht_fuer_ein_eigenes_Turnier()
    {
        // Sie ist für den anderen nicht vorhanden (ADR-0004) — und ein Turnier
        // auf ein Format zu gründen, das man nicht sehen darf, ergäbe eines,
        // dessen Modus man nie zu Gesicht bekäme.
        var fremder = _factory.CreateClientAs($"fremder-{Guid.NewGuid():N}");
        var vorlage = await CreatedIdAsync(await fremder.PostAsJsonAsync(
            "/api/format-templates",
            new SaveFormatTemplateRequest(BuiltInFormats.Knockout with
            {
                Id = $"fremd-{Guid.NewGuid():N}",
                Name = $"Fremde Vorlage {Guid.NewGuid():N}",
            }),
            Json));

        var ich = _factory.CreateClientAs($"leitung-{Guid.NewGuid():N}");

        var response = await ich.PostAsJsonAsync(
            "/api/tournaments",
            new CreateTournamentRequest(
                "Clubmeisterschaft",
                "TC Test",
                null,
                "Maria Alm",
                "Europe/Vienna",
                Discipline.Singles,
                new DateOnly(2026, 5, 16),
                new DateOnly(2026, 5, 17),
                vorlage),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Platzzeiten_lassen_sich_auf_einzelne_Plaetze_beschraenken()
    {
        // Der Flutlichtplatz ist länger offen als die übrigen. Ohne die Auswahl
        // bekäme jeder Platz dieselbe Zeit — und der Spielplan plante auf Plätzen
        // weiter, die längst dunkel sind.
        var turnier = await _factory.NeuesTurnierAsync(
            $"platzwart-{Guid.NewGuid():N}",
            new TurnierWunsch { Plaetze = 2, Auslosen = false });

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/courts/windows",
            new CreateCourtWindowsRequest(
                new TimeOnly(18, 0), new TimeOnly(22, 0), [turnier.CourtIds[0]]),
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await turnier.Admin.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{turnier.TournamentId}", Json);

        Assert.NotEmpty(detail!.Courts.Single(c => c.Id == turnier.CourtIds[0]).Windows);
        Assert.Empty(detail.Courts.Single(c => c.Id == turnier.CourtIds[1]).Windows);
    }

    [Fact]
    public async Task Eine_Phase_ohne_Namen_heisst_wie_ihr_Format()
    {
        // In der Vorlagenübersicht steht sonst nichts, woran sich ein Format
        // erkennen ließe.
        var client = _factory.CreateClientAs($"veranstalter-{Guid.NewGuid():N}");

        var definition = BuiltInFormats.Knockout with
        {
            Id = $"namenlos-{Guid.NewGuid():N}",
            Name = $"Ohne Phasennamen {Guid.NewGuid():N}",
            Phases = [BuiltInFormats.Knockout.Phases[0] with { Name = null }],
        };

        await CreatedIdAsync(await client.PostAsJsonAsync(
            "/api/format-templates", new SaveFormatTemplateRequest(definition), Json));

        var liste = await client.GetFromJsonAsync<List<FormatTemplateSummary>>(
            "/api/format-templates", Json);

        Assert.Equal(["Knockout"], liste!.Single(v => v.Name == definition.Name).Phases);
    }

    [Fact]
    public async Task Ein_Import_ohne_Adressen_legt_Spieler_ohne_Kontakt_an()
    {
        // Der häufigste Fall: eine Liste aus dem Vereinsheft, zwei Spalten.
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 2, Auslosen = false });

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/entries/import",
            new ImportEntriesRequest("Anna;Berger\nEva;Huber\n"),
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bericht = await response.Content.ReadFromJsonAsync<ImportEntriesResult>(Json);

        Assert.Equal(2, bericht!.Imported);
        Assert.Empty(bericht.Problems);
    }

    [Fact]
    public async Task Eine_krumme_Zeile_hinterlaesst_keinen_Spieler()
    {
        // Die zweite Zeile scheitert erst, nachdem ihr Spieler aufgelöst wurde.
        // Bliebe er stehen, hätte die Datei bei jedem neuen Versuch einen mehr —
        // samt Adresse.
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 2, Auslosen = false });

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/entries/import",
            new ImportEntriesRequest("Anna;Berger;anna@example.invalid\nAnna;Berger;anna@example.invalid\n"),
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bericht = await response.Content.ReadFromJsonAsync<ImportEntriesResult>(Json);

        Assert.Equal(1, bericht!.Imported);

        var treffer = await turnier.Admin.GetFromJsonAsync<List<PlayerSummary>>(
            "/api/players?q=Berger", Json);

        Assert.Single(treffer!, spieler => spieler.DisplayName == "Berger, Anna");
    }
}
