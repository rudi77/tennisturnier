using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Membership;
using TennisTurnier.Application.Social;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Social;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der Feed eines Turniers (ADR-0014).
///
/// Zwei Hälften, und beide werden hier geprüft. Die Chronik entsteht von
/// selbst — sie ist das, was den leeren Kasten füllt, bevor jemand schreibt.
/// Die Beiträge sind das, wofür die Vereine bisher die Anwendung verlassen und
/// daneben eine WhatsApp-Gruppe aufgemacht haben.
///
/// Und die Grenze: der Feed ist die Innenansicht der Gruppe. Wer nicht
/// dazugehört, bekommt 404 — nicht eine leere Liste, denn die wäre die Aussage
/// „dieses Turnier hat noch nichts geschrieben".
/// </summary>
public sealed class TurnierfeedApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public TurnierfeedApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private Task<AufgebautesTurnier> TurnierAsync(bool auslosen = true) =>
        _factory.NeuesTurnierAsync(
            $"feed-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 4, Auslosen = auslosen });

    // --- Die Chronik ------------------------------------------------------

    [Fact]
    public async Task Der_Feed_erzaehlt_den_Verlauf_ohne_dass_jemand_schreibt()
    {
        var aufbau = await TurnierAsync();

        var feed = await FeedAsync(aufbau.Admin, aufbau.TournamentId);

        // Meldung offen, Meldeschluss, Draw — in dieser Reihenfolge geschehen,
        // jüngstes zuerst ausgeliefert.
        Assert.Contains(feed.Posts, p => p.Kind == PostKind.DrawGenerated);
        Assert.Contains(feed.Posts, p => p.Kind == PostKind.StateChanged);
        Assert.All(feed.Posts, p => Assert.Null(p.Author));
    }

    [Fact]
    public async Task Der_Draw_nennt_die_Groesse_des_Feldes()
    {
        var aufbau = await TurnierAsync();

        var feed = await FeedAsync(aufbau.Admin, aufbau.TournamentId);
        var draw = feed.Posts.First(p => p.Kind == PostKind.DrawGenerated);

        Assert.Equal("Der Draw steht — 4 im Feld.", draw.Text);
    }

    [Fact]
    public async Task Ein_Ergebnis_steht_mit_Sieger_und_Stand_im_Feed()
    {
        var aufbau = await TurnierAsync();
        var phase = Assert.Single(await PhasenAsync(aufbau.Admin, aufbau.TournamentId));
        var match = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).First();

        await aufbau.Admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        var feed = await FeedAsync(aufbau.Admin, aufbau.TournamentId);
        var ergebnis = feed.Posts.First(p => p.Kind == PostKind.ResultRecorded);

        Assert.Contains("schlägt", ergebnis.Text);
        Assert.EndsWith("6:4 6:2", ergebnis.Text);
        Assert.Equal(match.Id, ergebnis.MatchId);
    }

    /// <summary>
    /// Der Stand wird aus Sicht des Siegers geschrieben. Steht er auf Seite
    /// zwei, werden die Sätze gedreht — ein „4:6" hinter „schlägt" wäre schlicht
    /// falsch herum.
    /// </summary>
    [Fact]
    public async Task Der_Stand_steht_aus_Sicht_des_Siegers()
    {
        var aufbau = await TurnierAsync();
        var phase = Assert.Single(await PhasenAsync(aufbau.Admin, aufbau.TournamentId));
        var match = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).First();

        // Seite zwei gewinnt.
        await aufbau.Admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(4, 6), new SetScore(2, 6)]),
            Json);

        var feed = await FeedAsync(aufbau.Admin, aufbau.TournamentId);
        var ergebnis = feed.Posts.First(p => p.Kind == PostKind.ResultRecorded);

        Assert.EndsWith("6:4 6:2", ergebnis.Text);
    }

    /// <summary>
    /// Eine Korrektur löscht nicht, sie schreibt daneben. Eine Chronik, die
    /// sich rückwirkend ändert, ist keine (ADR-0014).
    /// </summary>
    [Fact]
    public async Task Eine_Korrektur_schreibt_eine_zweite_Zeile_statt_die_erste_zu_aendern()
    {
        var aufbau = await TurnierAsync();
        var phase = Assert.Single(await PhasenAsync(aufbau.Admin, aufbau.TournamentId));
        var match = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).First();

        await aufbau.Admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        await aufbau.Admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 3)]),
            Json);

        var feed = await FeedAsync(aufbau.Admin, aufbau.TournamentId);
        var ergebnisse = feed.Posts.Where(p => p.Kind == PostKind.ResultRecorded).ToList();

        Assert.Equal(2, ergebnisse.Count);
        Assert.Contains(ergebnisse, p => p.Text.EndsWith("6:4 6:2", StringComparison.Ordinal));
        Assert.Contains(ergebnisse, p => p.Text.EndsWith("6:4 6:3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ein_Ereignis_laesst_sich_nicht_zuruecknehmen()
    {
        var aufbau = await TurnierAsync();
        var feed = await FeedAsync(aufbau.Admin, aufbau.TournamentId);
        var ereignis = feed.Posts.First(p => p.Kind != PostKind.Message);

        Assert.False(ereignis.CanDelete);

        var antwort = await aufbau.Admin.DeleteAsync($"/api/feed/{ereignis.Id}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, antwort.StatusCode);
    }

    // --- Die Beiträge -----------------------------------------------------

    [Fact]
    public async Task Die_Turnierleitung_schreibt_und_der_Beitrag_steht_oben()
    {
        var aufbau = await TurnierAsync();

        var post = await SchreibenAsync(aufbau.Admin, aufbau.TournamentId, "Platz 3 ist nass.");

        Assert.Equal(PostKind.Message, post.Kind);
        Assert.NotNull(post.Author);
        Assert.True(post.CanDelete);

        var feed = await FeedAsync(aufbau.Admin, aufbau.TournamentId);
        Assert.Equal(post.Id, feed.Posts[0].Id);
    }

    [Fact]
    public async Task Ein_leerer_Beitrag_ist_keiner()
    {
        var aufbau = await TurnierAsync();

        var antwort = await aufbau.Admin.PostAsJsonAsync(
            $"/api/tournaments/{aufbau.TournamentId}/feed", new WritePostRequest("   "), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, antwort.StatusCode);
    }

    [Fact]
    public async Task Ein_Mitglied_darf_schreiben_und_kommentieren()
    {
        var (leitung, tournamentId, mitglied) = await MitMitgliedAsync();

        var post = await SchreibenAsync(mitglied, tournamentId, "Komme zwanzig Minuten später.");
        Assert.Equal(PostKind.Message, post.Kind);

        var kommentar = await KommentierenAsync(leitung, post.Id, "Passt, du bist ohnehin erst dritt dran.");
        Assert.Equal("Passt, du bist ohnehin erst dritt dran.", kommentar.Text);

        var feed = await FeedAsync(mitglied, tournamentId);
        Assert.True(feed.CanWrite);

        var eigener = feed.Posts.Single(p => p.Id == post.Id);
        Assert.Single(eigener.Comments);

        // Das Mitglied darf seinen eigenen Beitrag zurücknehmen, den Kommentar
        // der Leitung darunter nicht.
        Assert.True(eigener.CanDelete);
        Assert.False(eigener.Comments[0].CanDelete);

        // Für die Turnierleitung ist es umgekehrt: sie darf beides — das ist
        // Moderation und in einer Vereinsgruppe gelegentlich nötig.
        var ausSichtDerLeitung = (await FeedAsync(leitung, tournamentId))
            .Posts.Single(p => p.Id == post.Id);

        Assert.True(ausSichtDerLeitung.CanDelete);
        Assert.True(ausSichtDerLeitung.Comments[0].CanDelete);
    }

    [Fact]
    public async Task Der_Verfasser_nimmt_seinen_Beitrag_zurueck()
    {
        var (_, tournamentId, mitglied) = await MitMitgliedAsync();
        var post = await SchreibenAsync(mitglied, tournamentId, "Doch nicht.");

        var antwort = await mitglied.DeleteAsync($"/api/feed/{post.Id}");
        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);

        var feed = await FeedAsync(mitglied, tournamentId);
        Assert.DoesNotContain(feed.Posts, p => p.Id == post.Id);
    }

    /// <summary>
    /// Moderation: fremdes Wort nimmt nur die Turnierleitung zurück — und in
    /// einer Vereinsgruppe ist genau das gelegentlich nötig.
    /// </summary>
    [Fact]
    public async Task Die_Turnierleitung_nimmt_auch_fremdes_zurueck()
    {
        var (leitung, tournamentId, mitglied) = await MitMitgliedAsync();
        var post = await SchreibenAsync(mitglied, tournamentId, "Unpassendes.");

        Assert.Equal(HttpStatusCode.NoContent, (await leitung.DeleteAsync($"/api/feed/{post.Id}")).StatusCode);
    }

    [Fact]
    public async Task Ein_Mitglied_nimmt_fremdes_nicht_zurueck()
    {
        var (leitung, tournamentId, mitglied) = await MitMitgliedAsync();
        var post = await SchreibenAsync(leitung, tournamentId, "Anweisung der Turnierleitung.");

        var antwort = await mitglied.DeleteAsync($"/api/feed/{post.Id}");

        // 404 und nicht 403: die Antwort soll nicht verraten, was es zu sehen
        // gäbe (ADR-0004).
        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    [Fact]
    public async Task Ein_Kommentar_laesst_sich_zuruecknehmen()
    {
        var (leitung, tournamentId, mitglied) = await MitMitgliedAsync();
        var post = await SchreibenAsync(mitglied, tournamentId, "Frage.");
        var kommentar = await KommentierenAsync(mitglied, post.Id, "Antwort, die weg soll.");

        var antwort = await mitglied.DeleteAsync($"/api/feed/{post.Id}/comments/{kommentar.Id}");
        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);

        var feed = await FeedAsync(leitung, tournamentId);
        Assert.Empty(feed.Posts.Single(p => p.Id == post.Id).Comments);
    }

    [Fact]
    public async Task Ein_unbekannter_Kommentar_laesst_sich_nicht_zuruecknehmen()
    {
        var (_, tournamentId, mitglied) = await MitMitgliedAsync();
        var post = await SchreibenAsync(mitglied, tournamentId, "Frage.");

        var antwort = await mitglied.DeleteAsync(
            $"/api/feed/{post.Id}/comments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    [Fact]
    public async Task Ein_Mitglied_nimmt_fremde_Kommentare_nicht_zurueck()
    {
        var (leitung, tournamentId, mitglied) = await MitMitgliedAsync();
        var post = await SchreibenAsync(mitglied, tournamentId, "Frage.");
        var kommentar = await KommentierenAsync(leitung, post.Id, "Wort der Leitung.");

        var antwort = await mitglied.DeleteAsync($"/api/feed/{post.Id}/comments/{kommentar.Id}");

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    [Fact]
    public async Task Ein_unbekannter_Eintrag_ist_nicht_zu_finden()
    {
        var (leitung, _, _) = await MitMitgliedAsync();

        var antwort = await leitung.PostAsJsonAsync(
            $"/api/feed/{Guid.NewGuid()}/comments", new WritePostRequest("Wohin?"), Json);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    /// <summary>
    /// Eine volle Seite heißt: es könnte mehr geben. Der Zeitstempel darin ist
    /// der Schlüssel für die nächste — eine Seitennummer verschöbe sich, sobald
    /// jemand dazwischen schreibt.
    /// </summary>
    [Fact]
    public async Task Eine_volle_Seite_nennt_den_Weg_zur_naechsten()
    {
        var (leitung, tournamentId, _) = await MitMitgliedAsync();
        await SchreibenAsync(leitung, tournamentId, "Erster.");

        // Die Uhr der Fabrik steht; in Wirklichkeit tut sie das nicht. Zwei
        // Beiträge mit demselben Zeitstempel wären ein Sonderfall, den der
        // Cursor nicht auflösen kann — hier soll das Blättern selbst geprüft
        // werden und nicht er.
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));

        await SchreibenAsync(leitung, tournamentId, "Zweiter.");

        var erste = (await leitung.GetFromJsonAsync<FeedPage>(
            $"/api/tournaments/{tournamentId}/feed?limit=1", Json))!;

        Assert.Single(erste.Posts);
        Assert.NotNull(erste.Before);

        var zweite = (await leitung.GetFromJsonAsync<FeedPage>(
            $"/api/tournaments/{tournamentId}/feed?limit=1&before={Uri.EscapeDataString(erste.Before!.Value.ToString("O"))}",
            Json))!;

        Assert.Single(zweite.Posts);
        Assert.NotEqual(erste.Posts[0].Id, zweite.Posts[0].Id);
    }

    /// <summary>
    /// Ein Zustandswechsel ohne Nachricht meldet nichts. „Abgebrochen" ist ein
    /// Verwaltungsvorgang; ein Feed, der jede Attributänderung mitschreibt,
    /// wird nicht gelesen.
    /// </summary>
    [Fact]
    public async Task Die_Wiedereroeffnung_meldet_sich_der_Abbruch_nicht()
    {
        var aufbau = await TurnierAsync();

        await aufbau.Admin.PostAsync(
            $"/api/tournaments/{aufbau.TournamentId}/registration/reopen", null);

        var nachOeffnung = await FeedAsync(aufbau.Admin, aufbau.TournamentId);
        Assert.Contains(nachOeffnung.Posts, p => p.Text.Contains("wieder offen", StringComparison.Ordinal));

        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/abandon", null);

        var nachAbbruch = await FeedAsync(aufbau.Admin, aufbau.TournamentId);
        Assert.Equal(nachOeffnung.Posts.Count, nachAbbruch.Posts.Count);
    }

    [Fact]
    public async Task Eine_Disqualifikation_steht_als_solche_im_Feed()
    {
        var aufbau = await TurnierAsync();
        var phase = Assert.Single(await PhasenAsync(aufbau.Admin, aufbau.TournamentId));
        var match = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).First();

        var antwort = await aufbau.Admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Disqualification, AffectedSide: 2),
            Json);
        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);

        var feed = await FeedAsync(aufbau.Admin, aufbau.TournamentId);
        var ergebnis = feed.Posts.First(p => p.Kind == PostKind.ResultRecorded);

        Assert.EndsWith("nach Disqualifikation", ergebnis.Text);
    }

    // --- Die Grenze -------------------------------------------------------

    [Fact]
    public async Task Wer_nicht_dazugehoert_findet_den_Feed_nicht()
    {
        var aufbau = await TurnierAsync();
        var fremder = _factory.CreateClientAs($"feed-fremd-{Guid.NewGuid():N}");

        var lesen = await fremder.GetAsync($"/api/tournaments/{aufbau.TournamentId}/feed");
        Assert.Equal(HttpStatusCode.NotFound, lesen.StatusCode);

        var schreiben = await fremder.PostAsJsonAsync(
            $"/api/tournaments/{aufbau.TournamentId}/feed", new WritePostRequest("Hallo?"), Json);
        Assert.Equal(HttpStatusCode.NotFound, schreiben.StatusCode);
    }

    [Fact]
    public async Task Ohne_Anmeldung_gibt_es_keinen_Feed()
    {
        var aufbau = await TurnierAsync();
        var anonym = _factory.CreateClient();

        var antwort = await anonym.GetAsync($"/api/tournaments/{aufbau.TournamentId}/feed");

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
    }

    [Fact]
    public async Task Der_Beitritt_meldet_sich_im_Feed()
    {
        var (leitung, tournamentId, _) = await MitMitgliedAsync();

        var feed = await FeedAsync(leitung, tournamentId);

        Assert.Contains(feed.Posts, p => p.Kind == PostKind.Joined);
    }

    // --- Aufbau -----------------------------------------------------------

    /// <summary>
    /// Ein Turnier mit offener Meldung, dem jemand über den Link beigetreten
    /// ist — der Normalfall einer Gruppe.
    /// </summary>
    private async Task<(HttpClient Leitung, Guid TournamentId, HttpClient Mitglied)> MitMitgliedAsync()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"feed-{Guid.NewGuid():N}", new TurnierWunsch { Auslosen = false });

        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/registration/open", null);

        var link = await aufbau.Admin.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{aufbau.TournamentId}/registration", Json);

        var mitglied = _factory.CreateClientAs(
            $"feed-mitglied-{Guid.NewGuid():N}", $"m{Guid.NewGuid():N}@example.invalid");

        var beitritt = await mitglied.PostAsJsonAsync(
            $"/api/join/{link!.Token}",
            new JoinRequest(Play: false, null, null, null, null, null, null, null),
            Json);

        Assert.True(beitritt.IsSuccessStatusCode, await beitritt.Content.ReadAsStringAsync());

        return (aufbau.Admin, aufbau.TournamentId, mitglied);
    }

    private static async Task<FeedPage> FeedAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<FeedPage>($"/api/tournaments/{tournamentId}/feed", Json))!;

    private static async Task<FeedPostView> SchreibenAsync(
        HttpClient client,
        Guid tournamentId,
        string text)
    {
        var antwort = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/feed", new WritePostRequest(text), Json);

        Assert.True(antwort.IsSuccessStatusCode, await antwort.Content.ReadAsStringAsync());

        return (await antwort.Content.ReadFromJsonAsync<FeedPostView>(Json))!;
    }

    private static async Task<FeedCommentView> KommentierenAsync(
        HttpClient client,
        Guid postId,
        string text)
    {
        var antwort = await client.PostAsJsonAsync(
            $"/api/feed/{postId}/comments", new WritePostRequest(text), Json);

        Assert.True(antwort.IsSuccessStatusCode, await antwort.Content.ReadAsStringAsync());

        return (await antwort.Content.ReadFromJsonAsync<FeedCommentView>(Json))!;
    }

    private static async Task<List<PhaseDetail>> PhasenAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json))!;
}
