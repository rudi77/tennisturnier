using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Membership;
using TennisTurnier.Application.Social;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der Kontaktgraph (ADR-0013).
///
/// Es gibt keine Freundschaftsanfrage: die Verbindungen entstehen aus
/// gespielten Matches und sind in dem Augenblick da, in dem das erste Ergebnis
/// eingetragen wird. Genau das wird hier geprüft — dass jemand, der spielt,
/// danach Kontakte hat, ohne irgendwo einen Knopf gedrückt zu haben.
/// </summary>
public sealed class KontakteApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public KontakteApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Wer_nichts_gespielt_hat_hat_keine_Kontakte()
    {
        var neu = _factory.CreateClientAs($"kontakt-neu-{Guid.NewGuid():N}");

        var kontakte = await KontakteAsync(neu);

        Assert.Empty(kontakte);
    }

    [Fact]
    public async Task Ohne_Anmeldung_gibt_es_keine_Kontakte()
    {
        var anonym = _factory.CreateClient();

        var antwort = await anonym.GetAsync("/api/me/connections");

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
    }

    /// <summary>
    /// Der ganze Weg: beitreten, mitspielen, ein Ergebnis — und danach steht
    /// der Gegner in der Kontaktliste, ohne dass jemand ihn hinzugefügt hat.
    /// </summary>
    [Fact]
    public async Task Ein_gespieltes_Match_macht_aus_dem_Gegner_einen_Kontakt()
    {
        var (leitung, tournamentId, spieler, _) = await GespieltAsync();

        var kontakte = await KontakteAsync(spieler);
        var gegner = Assert.Single(kontakte);

        Assert.Equal(1, gegner.Against);
        Assert.Equal(0, gegner.Together);
        Assert.Equal(1, gegner.SharedTournaments);
        Assert.Equal("Kontaktturnier", gegner.LastTournamentName);

        // Ein Sieg auf der einen Seite ist eine Niederlage auf der anderen.
        Assert.Equal(1, gegner.Won + gegner.Lost);

        await leitung.GetAsync($"/api/tournaments/{tournamentId}");
    }

    [Fact]
    public async Task Die_Zaehlung_gilt_fuer_beide_Seiten()
    {
        var (_, _, spieler, gegner) = await GespieltAsync();

        var meine = Assert.Single(await KontakteAsync(spieler));
        var seine = Assert.Single(await KontakteAsync(gegner));

        // Wer hier gewinnt, verliert dort — und umgekehrt.
        Assert.Equal(meine.Won, seine.Lost);
        Assert.Equal(meine.Lost, seine.Won);
    }

    /// <summary>
    /// Ein Doppel bringt drei Verbindungen mit einem Match: den Partner auf
    /// derselben Seite und zwei Gegner auf der anderen.
    /// </summary>
    [Fact]
    public async Task Ein_Doppel_nennt_den_Partner_getrennt_von_den_Gegnern()
    {
        var (spieler, _) = await DoppelGespieltAsync();

        var kontakte = await KontakteAsync(spieler);

        Assert.Equal(3, kontakte.Count);
        Assert.Single(kontakte, k => k.Together == 1 && k.Against == 0);
        Assert.Equal(2, kontakte.Count(k => k.Against == 1 && k.Together == 0));
    }

    // --- Aufbau -----------------------------------------------------------

    /// <summary>
    /// Zwei Konten treten einem Turnier bei, melden sich, und die Leitung lost
    /// aus und trägt das Ergebnis ein. Über HTTP und nicht über die
    /// Repositories — der Kontaktgraph hängt am Query-Filter, und ein Test, der
    /// die Datenbank direkt füllt, übersprünge genau das.
    /// </summary>
    private async Task<(HttpClient Leitung, Guid TournamentId, HttpClient Einer, HttpClient Anderer)>
        GespieltAsync()
    {
        var (leitung, tournamentId, token) = await OffenAsync(Discipline.Singles);

        var einer = await BeitretenAsync(token, "Anna");
        var anderer = await BeitretenAsync(token, "Berta");

        await AuslosenUndSpielenAsync(leitung, tournamentId);

        return (leitung, tournamentId, einer, anderer);
    }

    private async Task<(HttpClient Spieler, Guid TournamentId)> DoppelGespieltAsync()
    {
        var (leitung, tournamentId, token) = await OffenAsync(Discipline.Doubles);

        var einer = await BeitretenAsync(token, "Anna", partner: "Paula");
        await BeitretenAsync(token, "Berta", partner: "Bea");

        await AuslosenUndSpielenAsync(leitung, tournamentId);

        return (einer, tournamentId);
    }

    private async Task<(HttpClient Leitung, Guid TournamentId, string Token)> OffenAsync(
        Discipline disziplin)
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"kontakt-leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Name = "Kontaktturnier", Disziplin = disziplin, Auslosen = false });

        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/registration/open", null);

        var link = await aufbau.Admin.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{aufbau.TournamentId}/registration", Json);

        return (aufbau.Admin, aufbau.TournamentId, link!.Token);
    }

    private async Task<HttpClient> BeitretenAsync(string token, string vorname, string? partner = null)
    {
        var nachname = $"K{Guid.NewGuid():N}"[..10];

        var client = _factory.CreateClientAs(
            $"kontakt-{Guid.NewGuid():N}", $"{nachname.ToLowerInvariant()}@example.invalid");

        var antwort = await client.PostAsJsonAsync(
            $"/api/join/{token}",
            new JoinRequest(
                Play: true,
                vorname,
                nachname,
                null,
                partner,
                partner is null ? null : $"P{Guid.NewGuid():N}"[..10],
                partner is null ? null : $"p{Guid.NewGuid():N}@example.invalid",
                null),
            Json);

        Assert.True(antwort.IsSuccessStatusCode, await antwort.Content.ReadAsStringAsync());

        return client;
    }

    private static async Task AuslosenUndSpielenAsync(HttpClient leitung, Guid tournamentId)
    {
        // Wer selbst beitritt, ist gemeldet und noch nicht im Feld: die
        // Turnierleitung entscheidet, wer nachrückt (ADR-0012).
        var meldungen = await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json);

        foreach (var meldung in meldungen!)
        {
            await leitung.PostAsync(
                $"/api/tournaments/{tournamentId}/entries/{meldung.Id}/accept", null);
        }

        await leitung.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);

        var auslosen = await leitung.PostAsync($"/api/tournaments/{tournamentId}/draw", null);
        Assert.True(auslosen.IsSuccessStatusCode, await auslosen.Content.ReadAsStringAsync());

        var phasen = await leitung.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json);

        var match = phasen!.SelectMany(p => p.Matches).Single(m => m.Status == MatchStatus.Ready);

        var ergebnis = await leitung.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        Assert.Equal(HttpStatusCode.NoContent, ergebnis.StatusCode);
    }

    private static async Task<List<ConnectionView>> KontakteAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ConnectionView>>("/api/me/connections", Json))!;
}
