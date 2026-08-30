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

    /// <summary>
    /// Ein zweites Match gegen denselben Menschen zählt hoch — und nennt
    /// weiterhin das jüngste Turnier, nicht das erste.
    /// </summary>
    [Fact]
    public async Task Ein_zweites_Match_zaehlt_beim_selben_Kontakt_hoch()
    {
        var (leitung, tournamentId, einer, anderer) = await GespieltAsync();

        // Dieselben beiden noch einmal, in einem zweiten Turnier: die Meldung
        // findet ihren Spieler über das Konto wieder (ADR-0012).
        await NochEinmalAsync(einer, anderer);

        var kontakt = Assert.Single(await KontakteAsync(einer));

        Assert.Equal(2, kontakt.Against);
        Assert.Equal(2, kontakt.SharedTournaments);

        await leitung.GetAsync($"/api/tournaments/{tournamentId}");
    }

    /// <summary>
    /// Ohne Termin und ohne Platzzuweisung gibt es kein Datum. Der Kontakt
    /// steht trotzdem in der Liste — sie ordnet ihn ans Ende und lässt ihn
    /// nicht weg.
    /// </summary>
    [Fact]
    public async Task Ein_Kontakt_ohne_Datum_faellt_nicht_aus_der_Liste()
    {
        var (leitung, tournamentId, token) = await OffenAsync(Discipline.Singles, ohneTermin: true);

        var einer = await BeitretenAsync(token, "Anna");
        await BeitretenAsync(token, "Berta");

        await AuslosenUndSpielenAsync(leitung, tournamentId);

        var kontakt = Assert.Single(await KontakteAsync(einer));

        Assert.Null(kontakt.LastPlayedOn);
        Assert.Equal(1, kontakt.Against);
    }

    /// <summary>
    /// Lief das Match über einen Platz, kommt das Datum von dort — und nicht
    /// vom Beginn des Turniers.
    /// </summary>
    [Fact]
    public async Task Ein_Match_am_Platz_datiert_den_Kontakt_auf_den_Tag()
    {
        var (leitung, tournamentId, token) = await OffenAsync(
            Discipline.Singles, mitPlaetzen: true);

        var einer = await BeitretenAsync(token, "Anna");
        await BeitretenAsync(token, "Berta");

        await AnnehmenAsync(leitung, tournamentId);
        await leitung.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);
        await leitung.PostAsync($"/api/tournaments/{tournamentId}/draw", null);

        await SpielplanAsync(leitung, tournamentId);
        await leitung.PostAsync($"/api/tournaments/{tournamentId}/scheduling/match-day", null);

        var board = await leitung.GetFromJsonAsync<List<CourtBoard>>(
            $"/api/tournaments/{tournamentId}/courts", Json);

        var slot = board!.SelectMany(court => court.Queue).First();

        await leitung.PostAsync($"/api/assignments/{slot.AssignmentId}/call", null);
        await leitung.PostAsync($"/api/assignments/{slot.AssignmentId}/start", null);
        await leitung.PostAsync($"/api/assignments/{slot.AssignmentId}/finish", null);

        var ergebnis = await leitung.PutAsJsonAsync(
            $"/api/matches/{slot.MatchId}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);
        Assert.Equal(HttpStatusCode.NoContent, ergebnis.StatusCode);

        var kontakt = Assert.Single(await KontakteAsync(einer));

        Assert.NotNull(kontakt.LastPlayedOn);
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

    /// <summary>
    /// Ein zweites Turnier mit denselben beiden Konten. Sie melden sich über
    /// den Link, und die Auflösung findet ihre Spieler über das Konto wieder.
    /// </summary>
    private async Task NochEinmalAsync(HttpClient einer, HttpClient anderer)
    {
        var (leitung, tournamentId, token) = await OffenAsync(Discipline.Singles);

        foreach (var client in new[] { einer, anderer })
        {
            var antwort = await client.PostAsJsonAsync(
                $"/api/join/{token}",
                new JoinRequest(Play: true, null, null, null, null, null, null, null),
                Json);

            Assert.True(antwort.IsSuccessStatusCode, await antwort.Content.ReadAsStringAsync());
        }

        await AuslosenUndSpielenAsync(leitung, tournamentId);
    }

    private async Task<(HttpClient Leitung, Guid TournamentId, string Token)> OffenAsync(
        Discipline disziplin,
        bool ohneTermin = false,
        bool mitPlaetzen = false)
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"kontakt-leitung-{Guid.NewGuid():N}",
            new TurnierWunsch
            {
                Name = "Kontaktturnier",
                Disziplin = disziplin,
                Auslosen = false,
                Beginn = ohneTermin ? null : new DateOnly(2026, 5, 16),
                Ende = ohneTermin ? null : new DateOnly(2026, 5, 17),
                Plaetze = mitPlaetzen ? 1 : 0,
                Platzzeiten = mitPlaetzen,
                Uhr = mitPlaetzen
                    ? new DateTimeOffset(2026, 5, 16, 8, 0, 0, TimeSpan.FromHours(2))
                    : null,
            });

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

    /// <summary>
    /// Wer selbst beitritt, ist gemeldet und noch nicht im Feld: die
    /// Turnierleitung entscheidet, wer nachrückt (ADR-0012).
    /// </summary>
    private static async Task AnnehmenAsync(HttpClient leitung, Guid tournamentId)
    {
        var meldungen = await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json);

        foreach (var meldung in meldungen!)
        {
            await leitung.PostAsync(
                $"/api/tournaments/{tournamentId}/entries/{meldung.Id}/accept", null);
        }
    }

    /// <summary>Rechnen und Bestätigen bleiben zwei Schritte (ADR-0002).</summary>
    private static async Task SpielplanAsync(HttpClient leitung, Guid tournamentId)
    {
        var antwort = await leitung.PostAsync(
            $"/api/tournaments/{tournamentId}/schedule/proposal", null);

        Assert.True(antwort.IsSuccessStatusCode, await antwort.Content.ReadAsStringAsync());

        var vorschlag = (await antwort.Content.ReadFromJsonAsync<SchedulePlanResult>(Json))!;

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            new ConfirmScheduleRequest([.. vorschlag.Assignments.Select(a => new ConfirmedAssignment(
                a.MatchId, a.CourtId, a.SequenceOnCourt, a.PlannedStart, a.EstimatedDuration))]),
            Json);
    }

    private static async Task AuslosenUndSpielenAsync(HttpClient leitung, Guid tournamentId)
    {
        await AnnehmenAsync(leitung, tournamentId);

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
