using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die öffentliche Ansicht und der Zustand dahinter dürfen nicht auseinander
/// laufen.
///
/// Das ist die eigentliche Gefahr eines getrennten Lesemodells (ADR-0003): eine
/// Projektion, die hinterherhinkt, meldet keinen Fehler. Sie zeigt nur etwas
/// anderes als die Wahrheit, und niemand merkt es, bis jemand nachrechnet.
/// </summary>
public sealed class PublicViewConsistencyTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public PublicViewConsistencyTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static RecordResultRequest Win() =>
        new(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]);

    /// <summary>
    /// Trägt ein erstes Ergebnis ein, damit das Turnier läuft.
    ///
    /// Ohne diesen Schritt stößt jede parallele Eingabe zuerst auf den Zähler des
    /// Turniers — das erste Ergebnis macht aus einem ausgelosten ein laufendes —,
    /// und die Anfragen kollidieren, bevor sie die Projektion überhaupt
    /// erreichen. Genau das würde die Behauptung dieses Tests verdecken.
    /// </summary>
    private static async Task StartAsync(HttpClient admin, MatchDetail match)
    {
        var response = await admin.PutAsJsonAsync($"/api/matches/{match.Id}/result", Win(), Json);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<(HttpClient Admin, Guid TournamentId)> DrawnAsync(int participants = 8)
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            "konsistenz-admin",
            new TurnierWunsch
            {
                Anlage = "TC Konsistenz",
                Teilnehmer = participants,
                Plaetze = 1,
                Oeffentlich = true,
            });

        return (aufbau.Admin, aufbau.TournamentId);
    }

    private static async Task<List<PhaseDetail>> PhasesAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json))!;

    private async Task<JsonElement> PublicViewAsync(Guid tournamentId) =>
        await _factory.CreateClient().GetFromJsonAsync<JsonElement>(
            $"/public/tournaments/{tournamentId}", Json);

    private static int ResultsIn(JsonElement view) =>
        view.GetProperty("phases").EnumerateArray()
            .SelectMany(phase => phase.GetProperty("matches").EnumerateArray())
            .Count(match => match.TryGetProperty("score", out _));

    [Fact]
    public async Task Gleichzeitige_Ergebnisse_gehen_der_oeffentlichen_Ansicht_nicht_verloren()
    {
        // Regression: der Neuaufbau lief als zweites, eigenes Speichern auf
        // demselben Änderungsverfolger. Ein Request sah die inzwischen
        // festgeschriebenen Ergebnisse paralleler Requests nicht, schrieb seinen
        // unvollständigen Stand aber als letzter fest — lautlos, mit passendem
        // ETag, und dauerhaft, bis zufällig jemand dasselbe Turnier erneut
        // anfasste. Beim letzten Match hieße das: das Endergebnis erscheint nie.
        var (admin, tournamentId) = await DrawnAsync(participants: 16);
        var ready = (await PhasesAsync(admin, tournamentId)).Single()
            .Matches.Where(m => m.Status == MatchStatus.Ready).ToList();

        await StartAsync(admin, ready[0]);

        var responses = await Task.WhenAll(ready.Skip(1).Select(match =>
            admin.PutAsJsonAsync($"/api/matches/{match.Id}/result", Win(), Json)));

        // Ein Konflikt ist erlaubt — er bedeutet dann aber auch, dass nichts
        // gespeichert wurde. Nur die erfolgreichen zählen.
        Assert.All(responses, response => Assert.Contains(
            response.StatusCode,
            new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict }));

        var stored = (await PhasesAsync(admin, tournamentId)).Single()
            .Matches.Count(m => m.Score is not null);

        Assert.Equal(stored, ResultsIn(await PublicViewAsync(tournamentId)));
    }

    [Fact]
    public async Task Ein_Konflikt_hinterlaesst_kein_gespeichertes_Ergebnis()
    {
        // Regression: der zweite Speichervorgang für die Projektion meldete dem
        // Aufrufer einen Konflikt, obwohl sein Ergebnis längst festgeschrieben
        // war. Wer der Aufforderung folgte und es wiederholte, überschrieb damit
        // ein bereits ausgewertetes Ergebnis. Ein 409 muss heißen: nichts
        // passiert, bitte neu laden.
        var (admin, tournamentId) = await DrawnAsync(participants: 16);
        var ready = (await PhasesAsync(admin, tournamentId)).Single()
            .Matches.Where(m => m.Status == MatchStatus.Ready).ToList();

        await StartAsync(admin, ready[0]);

        var matches = ready.Skip(1).ToList();
        var responses = await Task.WhenAll(matches.Select(match =>
            admin.PutAsJsonAsync($"/api/matches/{match.Id}/result", Win(), Json)));

        var after = (await PhasesAsync(admin, tournamentId)).Single().Matches;

        for (var index = 0; index < matches.Count; index++)
        {
            var stored = after.Single(m => m.Id == matches[index].Id).Score;

            if (responses[index].StatusCode == HttpStatusCode.Conflict)
            {
                Assert.Null(stored);
            }
            else
            {
                Assert.NotNull(stored);
            }
        }
    }

    [Fact]
    public async Task Ein_umbenannter_Platz_heisst_auch_oeffentlich_anders()
    {
        // Regression: die Platzverwaltung kannte die öffentliche Ansicht nicht.
        // Am Turniertag schickte der Aushang die Zuschauer damit auf einen
        // Platz, den es unter dem Namen nicht mehr gibt.
        var (admin, tournamentId) = await DrawnAsync();
        var match = (await PhasesAsync(admin, tournamentId)).Single()
            .Matches.First(m => m.Status == MatchStatus.Ready);

        var courts = await admin.PlaetzeAsync(tournamentId);
        var courtId = courts!.Single().Id;

        await admin.PostAsJsonAsync(
            $"/api/matches/{match.Id}/court", new AssignCourtRequest(courtId, 1, null, null, null), Json);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync(
                $"/api/tournaments/{tournamentId}/courts/{courtId}",
                new UpdateCourtRequest("Center Court", IsCenterCourt: true, IsActive: true),
                Json)).StatusCode);

        var view = await PublicViewAsync(tournamentId);

        Assert.Equal("Center Court", view.GetProperty("courts").EnumerateArray().Single()
            .GetProperty("name").GetString());

        Assert.Equal("Center Court", view.GetProperty("phases").EnumerateArray().Single()
            .GetProperty("matches").EnumerateArray()
            .Single(m => m.GetProperty("id").GetGuid() == match.Id)
            .GetProperty("courtName").GetString());
    }

    [Fact]
    public async Task Ein_umbenannter_Ort_heisst_auch_oeffentlich_anders()
    {
        // Der Ort steht am Turnier und nicht mehr in eigenen Stammdaten. Genau
        // deshalb muss die Änderung durch dieselbe Kette laufen wie jede andere
        // — sonst nennt der Aushang eine Anlage, die niemand mehr so kennt.
        var (admin, tournamentId) = await DrawnAsync();
        var detail = (await admin.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json))!;

        await admin.PutAsJsonAsync(
            $"/api/tournaments/{tournamentId}",
            new UpdateTournamentRequest(
                detail.Name,
                "TC Neuer Name",
                detail.Venue.Address,
                detail.Venue.City,
                detail.Venue.TimeZoneId,
                detail.Discipline,
                detail.StartsOn,
                detail.EndsOn),
            Json);

        Assert.Equal(
            "TC Neuer Name",
            (await PublicViewAsync(tournamentId)).GetProperty("venueName").GetString());
    }

    [Fact]
    public async Task Ein_stillgelegter_Platz_laesst_sich_nicht_belegen()
    {
        // Regression: ein stillgelegter Platz verschwand aus der öffentlichen
        // Platzliste, das Match behielt aber seinen Namen. Es stand damit
        // öffentlich auf einem Platz, den die Liste nicht kennt — und die
        // Warteschlange, die am Turniertag die eigentliche Aussage ist, fehlte.
        var (admin, tournamentId) = await DrawnAsync();
        var match = (await PhasesAsync(admin, tournamentId)).Single()
            .Matches.First(m => m.Status == MatchStatus.Ready);

        var courts = await admin.PlaetzeAsync(tournamentId);
        var courtId = courts!.Single().Id;

        await admin.PutAsJsonAsync(
            $"/api/tournaments/{tournamentId}/courts/{courtId}",
            new UpdateCourtRequest("Platz 1", IsCenterCourt: false, IsActive: false),
            Json);

        var response = await admin.PostAsJsonAsync(
            $"/api/matches/{match.Id}/court", new AssignCourtRequest(courtId, 1, null, null, null), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Ein_belegter_Platz_bleibt_sichtbar_auch_wenn_er_stillgelegt_wird()
    {
        var (admin, tournamentId) = await DrawnAsync();
        var match = (await PhasesAsync(admin, tournamentId)).Single()
            .Matches.First(m => m.Status == MatchStatus.Ready);

        var courts = await admin.PlaetzeAsync(tournamentId);
        var courtId = courts!.Single().Id;

        await admin.PostAsJsonAsync(
            $"/api/matches/{match.Id}/court", new AssignCourtRequest(courtId, 1, null, null, null), Json);

        await admin.PutAsJsonAsync(
            $"/api/tournaments/{tournamentId}/courts/{courtId}",
            new UpdateCourtRequest("Platz 1", IsCenterCourt: false, IsActive: false),
            Json);

        var view = await PublicViewAsync(tournamentId);
        var court = Assert.Single(view.GetProperty("courts").EnumerateArray().ToList());

        Assert.Equal(match.Id, court.GetProperty("queue").EnumerateArray().Single()
            .GetProperty("matchId").GetGuid());
    }

    [Fact]
    public async Task Ein_Rueckzug_nach_der_Auslosung_loescht_niemanden_aus_der_Tabelle()
    {
        // Regression: die Tabelle wurde aus den angenommenen Meldungen gerechnet,
        // der Baum aus den Matches. Wer eine Runde gewann und sich dann abmeldete,
        // fiel samt seiner Siege aus der Endplatzierung, stand aber weiter als
        // Sieger im Baum. Ein Rückzug nach der Auslosung wird als Nichtantreten
        // gewertet und verändert den Baum ausdrücklich nicht.
        var (admin, tournamentId) = await DrawnAsync();
        var phase = (await PhasesAsync(admin, tournamentId)).Single();
        var match = phase.Matches.First(m => m.Status == MatchStatus.Ready);

        await admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        var winner = match.Side1.EntryId!.Value;

        var before = await admin.GetFromJsonAsync<StandingsDetail>(
            $"/api/tournaments/{tournamentId}/phases/{phase.Id}/standings", Json);

        Assert.Contains(before!.Places, place => place.EntryId == winner);

        await admin.PostAsync($"/api/tournaments/{tournamentId}/entries/{winner}/withdraw", null);

        var after = await admin.GetFromJsonAsync<StandingsDetail>(
            $"/api/tournaments/{tournamentId}/phases/{phase.Id}/standings", Json);

        Assert.Equal(before.Places.Count, after!.Places.Count);
        Assert.Contains(after.Places, place => place.EntryId == winner);
    }
}
