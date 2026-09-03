using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Was die Ergebniseingabe abweist — und was mit einer Zuweisung geschieht,
/// die wieder verschwinden soll.
///
/// Die Absagen sind hier der Inhalt. Ein Ausgang, für den niemand angibt, wen
/// er betrifft, führt zu einem Baum, in dem der Falsche eine Runde weiter ist
/// — und das fällt erst im Finale auf.
/// </summary>
public sealed class ErgebnisAbsagenApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public ErgebnisAbsagenApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private async Task<(AufgebautesTurnier Turnier, Guid MatchId)> AusgelostesTurnierAsync()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 4, Plaetze = 2, Platzzeiten = true });

        var phasen = await turnier.Admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{turnier.TournamentId}/phases", Json);

        var match = phasen!
            .SelectMany(p => p.Matches)
            .First(m => m.Status == MatchStatus.Ready);

        return (turnier, match.Id);
    }

    private static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return problem.GetProperty("detail").GetString() ?? string.Empty;
    }

    /// <summary>
    /// Ein abgebrochenes Turnier nimmt keine Ergebnisse mehr an.
    ///
    /// Die Phasen bleiben beim Abbruch stehen, und die Ergebniseingabe fragte
    /// nur nach der Berechtigung. Ein Schiedsrichter konnte damit weiter
    /// eintragen, der Feed meldete es und die öffentliche Ansicht wurde neu
    /// gebaut — während das Turnier stillschweigend abgebrochen blieb.
    /// </summary>
    [Fact]
    public async Task Ein_abgebrochenes_Turnier_nimmt_kein_Ergebnis_mehr()
    {
        var (turnier, matchId) = await AusgelostesTurnierAsync();

        await turnier.Admin.PostAsync($"/api/tournaments/{turnier.TournamentId}/abandon", null);

        var response = await turnier.Admin.PutAsJsonAsync(
            $"/api/matches/{matchId}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 3)]),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(
            "nimmt keine Ergebnisse an",
            await DetailAsync(response),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_abgebrochenes_Turnier_laesst_auch_keine_Ruecknahme_zu()
    {
        // Die andere Richtung: erst eintragen, dann abbrechen. Auch das
        // Zurücknehmen läuft über Feed und öffentliche Ansicht und hätte den
        // Abbruch stillschweigend übergangen.
        var (turnier, matchId) = await AusgelostesTurnierAsync();

        await turnier.Admin.PutAsJsonAsync(
            $"/api/matches/{matchId}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 3)]),
            Json);

        await turnier.Admin.PostAsync($"/api/tournaments/{turnier.TournamentId}/abandon", null);

        var response = await turnier.Admin.DeleteAsync($"/api/matches/{matchId}/result");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Freilos_wird_nicht_eingetragen()
    {
        var (turnier, matchId) = await AusgelostesTurnierAsync();

        var response = await turnier.Admin.PutAsJsonAsync(
            $"/api/matches/{matchId}/result",
            new RecordResultRequest(MatchOutcome.Bye, null, null, 1),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(
            "beim Aufbau des Baums entschieden",
            await DetailAsync(response),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_unbekannter_Ausgang_wird_benannt()
    {
        var (turnier, matchId) = await AusgelostesTurnierAsync();

        var response = await turnier.Admin.PutAsJsonAsync(
            $"/api/matches/{matchId}/result",
            new RecordResultRequest((MatchOutcome)99, null, null, 1),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Unbekannter Ausgang", await DetailAsync(response), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MatchOutcome.Retirement)]
    [InlineData(MatchOutcome.Walkover)]
    [InlineData(MatchOutcome.Disqualification)]
    public async Task Wer_betroffen_ist_muss_dabeistehen(MatchOutcome ausgang)
    {
        var (turnier, matchId) = await AusgelostesTurnierAsync();

        var response = await turnier.Admin.PutAsJsonAsync(
            $"/api/matches/{matchId}/result",
            new RecordResultRequest(ausgang, null, null, AffectedSide: null),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(
            "welche Seite betroffen ist",
            await DetailAsync(response),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_Zuweisung_laesst_sich_wieder_wegnehmen()
    {
        var (turnier, matchId) = await AusgelostesTurnierAsync();

        var zuweisung = await turnier.Admin.PostAsJsonAsync(
            $"/api/matches/{matchId}/court",
            new AssignCourtRequest(
                turnier.CourtIds[0],
                SequenceOnCourt: 1,
                PlannedStart: null,
                EarliestStart: null,
                EstimatedDuration: TimeSpan.FromMinutes(60),
                Pinned: false),
            Json);

        Assert.Equal(HttpStatusCode.OK, zuweisung.StatusCode);
        var ergebnis = await zuweisung.Content.ReadFromJsonAsync<AssignCourtResult>(Json);

        var weg = await turnier.Admin.DeleteAsync($"/api/court-assignments/{ergebnis!.AssignmentId}");
        Assert.Equal(HttpStatusCode.NoContent, weg.StatusCode);

        // Und danach hängt keine mehr am Match.
        var phasen = await turnier.Admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{turnier.TournamentId}/phases", Json);

        Assert.Null(phasen!.SelectMany(p => p.Matches).Single(m => m.Id == matchId).Assignment);
    }

    [Fact]
    public async Task Eine_Zuweisung_die_es_nicht_gibt_ist_nicht_gefunden()
    {
        var (turnier, _) = await AusgelostesTurnierAsync();

        var response = await turnier.Admin.DeleteAsync($"/api/court-assignments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
