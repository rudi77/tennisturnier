using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Registration;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die letzten Wege, die im geraden Ablauf nicht vorkommen: ein Doppel, dessen
/// Partner keine Adresse hinterlässt, ein Match, das auf einem stillgelegten
/// Platz fortgesetzt werden soll, eine Gruppenphase, deren Ergebnis nach der
/// Endrunde noch korrigiert werden soll — und ein Systemadministrator, der
/// alles sieht und trotzdem nicht alles nehmen darf.
/// </summary>
public sealed class WeitereRandfaelleApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string AdminMail = "erster.admin@example.invalid";

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Ein_Doppelpartner_ohne_Adresse_wird_trotzdem_gemeldet()
    {
        // Wer sich zu zweit meldet, hat die Adresse des Partners oft nicht zur
        // Hand. Das darf die Meldung nicht verhindern — die Rückmeldung geht
        // dann eben nur an den, der sie abgeschickt hat.
        using var fabrik = new TennisTurnierApiFactory();

        var turnier = await fabrik.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teams = ["Berger / Huber"], Auslosen = false });

        var link = await turnier.Admin.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{turnier.TournamentId}/registration", Json);

        var response = await fabrik.CreateClient().PostAsJsonAsync(
            $"/public/registrations/{link!.Token}",
            new SelfRegistrationRequest(
                "Anna",
                "Neu",
                "anna.neu@example.invalid",
                null,
                "Eva",
                "Ohnemail",
                PartnerEmail: null,
                TeamName: null),
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Match_wird_nicht_auf_einem_stillgelegten_Platz_fortgesetzt()
    {
        using var fabrik = new TennisTurnierApiFactory();

        var turnier = await fabrik.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch
            {
                Teilnehmer = 4,
                Plaetze = 2,
                Platzzeiten = true,
                Spielplan = true,
                Turniertag = true,
            });

        var board = await turnier.Admin.GetFromJsonAsync<List<CourtBoard>>(
            $"/api/tournaments/{turnier.TournamentId}/courts", Json);

        var spielbar = board!.SelectMany(c => c.Queue).First(q => q.MatchStatus == MatchStatus.Ready);

        foreach (var schritt in new[] { "call", "start", "suspend" })
        {
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await turnier.Admin.PostAsync(
                    $"/api/assignments/{spielbar.AssignmentId}/{schritt}", null)).StatusCode);
        }

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/assignments/{spielbar.AssignmentId}/resume",
            new ResumeMatchRequest(Guid.NewGuid()),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Eine_Gruppe_laesst_sich_nicht_korrigieren_wenn_die_Endrunde_laeuft()
    {
        // Die Korrektur müsste die Endrunde umbesetzen — dort stünde dann
        // jemand, der laut korrigierter Tabelle nie hätte antreten dürfen. Wer
        // wirklich korrigieren will, rollt von hinten auf.
        using var fabrik = new TennisTurnierApiFactory();

        var turnier = await fabrik.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Vorlage = BuiltInFormats.GroupThenKnockout.Name, Teilnehmer = 8 });

        var phasen = await turnier.Admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{turnier.TournamentId}/phases", Json);

        var gruppe = phasen!.OrderBy(p => p.Ordinal).First();

        // Die ganze Gruppenphase ausspielen.
        foreach (var match in gruppe.Matches)
        {
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await turnier.Admin.PutAsJsonAsync(
                    $"/api/matches/{match.Id}/result",
                    new RecordResultRequest(
                        MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)], null, null),
                    Json)).StatusCode);
        }

        // Und ein Match der Endrunde.
        var endrunde = (await turnier.Admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{turnier.TournamentId}/phases", Json))!
            .OrderBy(p => p.Ordinal)
            .Last();

        var erstes = endrunde.Matches.First(m => m.Status == MatchStatus.Ready);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await turnier.Admin.PutAsJsonAsync(
                $"/api/matches/{erstes.Id}/result",
                new RecordResultRequest(
                    MatchOutcome.Normal, [new SetScore(6, 1), new SetScore(6, 1)], null, null),
                Json)).StatusCode);

        var response = await turnier.Admin.DeleteAsync($"/api/matches/{gruppe.Matches[0].Id}/result");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // Von hinten aufgerollt geht es: erst das Ergebnis der Endrunde
        // zurücknehmen, dann steht die Gruppe wieder offen.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await turnier.Admin.DeleteAsync($"/api/matches/{erstes.Id}/result")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await turnier.Admin.DeleteAsync($"/api/matches/{gruppe.Matches[0].Id}/result")).StatusCode);
    }

    [Fact]
    public async Task Auch_ein_Systemadministrator_nimmt_keine_fremde_Vorlage()
    {
        // Er sieht jede Vorlage — verwenden darf er trotzdem nur die eigenen und
        // die mitgelieferten. Sonst hinge sein eingefrorenes Format bis zur
        // Auslosung an einer Definition, die ein anderer noch ändern kann.
        using var fabrik = new TennisTurnierApiFactory([AdminMail]);

        var fremder = fabrik.CreateClientAs($"fremder-{Guid.NewGuid():N}");
        var vorlage = await CreatedIdAsync(await fremder.PostAsJsonAsync(
            "/api/format-templates",
            new SaveFormatTemplateRequest(BuiltInFormats.Knockout with
            {
                Id = $"fremd-{Guid.NewGuid():N}",
                Name = $"Fremde Vorlage {Guid.NewGuid():N}",
            }),
            Json));

        var admin = fabrik.CreateClientAs("admin-subject", AdminMail);

        // Er sieht sie.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/format-templates/{vorlage}")).StatusCode);

        var response = await admin.PostAsJsonAsync(
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
    public async Task Ein_Spielplan_fuer_ein_Match_das_es_nicht_gibt()
    {
        using var fabrik = new TennisTurnierApiFactory();

        var turnier = await fabrik.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 4, Plaetze = 2, Platzzeiten = true });

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/schedule/confirm",
            new ConfirmScheduleRequest(
                [
                    new ConfirmedAssignment(
                        Guid.NewGuid(),
                        turnier.CourtIds[0],
                        1,
                        new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.FromHours(2)),
                        TimeSpan.FromMinutes(60)),
                ]),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
