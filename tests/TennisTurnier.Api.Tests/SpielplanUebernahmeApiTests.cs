using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Was die Übernahme eines Spielplans aufräumt.
///
/// Eine Ansetzung muss verschwinden, deren Match es nach einer neuen Auslosung
/// nicht mehr gibt — sonst steht auf dem Aushang ein Match, das niemand spielt,
/// und ein Platz gilt als belegt, an dem nichts stattfindet.
/// </summary>
public sealed class SpielplanUebernahmeApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public SpielplanUebernahmeApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Eine_neue_Auslosung_nimmt_die_alten_Ansetzungen_mit()
    {
        // Der Weg, den eine Turnierleitung geht, wenn nach dem Auslosen noch
        // jemand nachrückt: Meldung wieder öffnen, neu auslosen, neu planen.
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 4, Plaetze = 2, Platzzeiten = true, Spielplan = true });

        var vorher = await turnier.Admin.GetFromJsonAsync<List<CourtBoard>>(
            $"/api/tournaments/{turnier.TournamentId}/courts", Json);

        Assert.NotEmpty(vorher!.SelectMany(c => c.Queue));

        foreach (var schritt in new[] { "registration/reopen", "registration/close", "draw" })
        {
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await turnier.Admin.PostAsync(
                    $"/api/tournaments/{turnier.TournamentId}/{schritt}", null)).StatusCode);
        }

        // Der neue Vorschlag kennt die alten Matches nicht mehr — mit seiner
        // Bestätigung verschwinden ihre Ansetzungen.
        var vorschlag = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/schedule/proposal", null);

        var plan = await vorschlag.Content.ReadFromJsonAsync<SchedulePlanResult>(Json);

        var bestaetigt = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/schedule/confirm",
            new ConfirmScheduleRequest(
                [.. plan!.Assignments.Select(a => new ConfirmedAssignment(
                    a.MatchId, a.CourtId, a.SequenceOnCourt, a.PlannedStart, a.EstimatedDuration))]),
            Json);

        Assert.Equal(HttpStatusCode.OK, bestaetigt.StatusCode);

        var nachher = await turnier.Admin.GetFromJsonAsync<List<CourtBoard>>(
            $"/api/tournaments/{turnier.TournamentId}/courts", Json);

        var alteMatches = vorher!.SelectMany(c => c.Queue).Select(q => q.MatchId).ToHashSet();
        var neueMatches = nachher!.SelectMany(c => c.Queue).Select(q => q.MatchId).ToHashSet();

        Assert.NotEmpty(neueMatches);
        Assert.Empty(neueMatches.Intersect(alteMatches));
    }

    [Fact]
    public async Task Ein_gespieltes_Match_verliert_seinen_Platz()
    {
        // Im Planungsbetrieb kann ein Ergebnis eintreffen, bevor der Plan neu
        // bestätigt wird — etwa weil zwei Teilnehmer schon gespielt haben. Die
        // Ansetzung dafür ist damit hinfällig: sie hielte Zeit für etwas frei,
        // das vorbei ist.
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 4, Plaetze = 2, Platzzeiten = true, Spielplan = true });

        var phasen = await turnier.Admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{turnier.TournamentId}/phases", Json);

        var gespielt = phasen!.SelectMany(p => p.Matches).First(m => m.Assignment is not null);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await turnier.Admin.PutAsJsonAsync(
                $"/api/matches/{gespielt.Id}/result",
                new RecordResultRequest(
                    MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)], null, null),
                Json)).StatusCode);

        var vorschlag = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/schedule/proposal", null);

        var plan = await vorschlag.Content.ReadFromJsonAsync<SchedulePlanResult>(Json);

        var bestaetigt = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/schedule/confirm",
            new ConfirmScheduleRequest(
                [.. plan!.Assignments.Select(a => new ConfirmedAssignment(
                    a.MatchId, a.CourtId, a.SequenceOnCourt, a.PlannedStart, a.EstimatedDuration))]),
            Json);

        Assert.Equal(HttpStatusCode.OK, bestaetigt.StatusCode);

        var danach = await turnier.Admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{turnier.TournamentId}/phases", Json);

        Assert.Null(danach!.SelectMany(p => p.Matches).Single(m => m.Id == gespielt.Id).Assignment);
    }
}
