using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Registration;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die Selbstmeldung über den öffentlichen Link.
///
/// Der eine Weg in dieser Anwendung, auf dem jemand ohne Konto etwas schreibt.
/// Autorisiert ist er allein durch das Token im Pfad — und damit ist er auch der
/// eine Weg, auf dem der Query-Filter aus ADR-0004 nicht greift. Was hier
/// schiefgeht, geht still schief: die Meldung wäre gespeichert und der Melder
/// bekäme 404.
/// </summary>
public sealed class OeffentlicheAnmeldungApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public OeffentlicheAnmeldungApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>
    /// Ein Turnier mit offener Meldung samt seinem Anmeldelink — und ein Client
    /// ohne jede Anmeldung, so wie ihn ein Melder benutzt.
    /// </summary>
    private async Task<(HttpClient Leitung, Guid TournamentId, string Token, HttpClient Anonym)> OffenAsync(
        Discipline disziplin = Discipline.Singles,
        int? kapazitaet = null,
        DateTimeOffset? meldeschluss = null)
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"melde-leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Anlage = "TC Anmeldung", Disziplin = disziplin, Auslosen = false });

        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/registration/open", null);

        if (kapazitaet is not null || meldeschluss is not null)
        {
            await aufbau.Admin.PutAsJsonAsync(
                $"/api/tournaments/{aufbau.TournamentId}/registration",
                new ConfigureRegistrationRequest(kapazitaet, meldeschluss),
                Json);
        }

        var link = await aufbau.Admin.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{aufbau.TournamentId}/registration", Json);

        return (aufbau.Admin, aufbau.TournamentId, link!.Token, _factory.CreateClient());
    }

    private static SelfRegistrationRequest Meldung(
        string nachname,
        string? partnerNachname = null,
        string? teamName = null) =>
        new(
            "Anna",
            nachname,
            $"{nachname.ToLowerInvariant()}@example.invalid",
            "+43 1 2345678",
            partnerNachname is null ? null : "Eva",
            partnerNachname,
            partnerNachname is null ? null : $"{partnerNachname.ToLowerInvariant()}@example.invalid",
            teamName);

    private static string Nachname() => $"Melder{Guid.NewGuid():N}"[..14];

    [Fact]
    public async Task Ein_Melder_ohne_Konto_sieht_den_Turnierkopf()
    {
        var (_, _, token, anonym) = await OffenAsync();

        var view = await anonym.GetFromJsonAsync<PublicRegistrationView>(
            $"/public/registrations/{token}", Json);

        Assert.NotNull(view);
        Assert.Equal("Clubmeisterschaft", view.TournamentName);
        Assert.Equal("TC Anmeldung", view.VenueName);
        Assert.Equal(Discipline.Singles, view.Discipline);
        Assert.False(view.NeedsPartner);
        Assert.True(view.IsOpen);
        Assert.Null(view.FreeSlots);
    }

    [Fact]
    public async Task Die_oeffentliche_Ansicht_des_Links_nennt_keine_Namen()
    {
        // Der Link darf kein Weg an der Projektion vorbei sein. Steht dort erst
        // eine Teilnehmerliste, ist die ganze Datensparsamkeit von ADR-0003
        // umgangen — und zwar von der Seite, die niemand prüft.
        var (_, _, token, anonym) = await OffenAsync();
        var nachname = Nachname();

        await anonym.PostAsJsonAsync($"/public/registrations/{token}", Meldung(nachname), Json);

        var raw = await (await anonym.GetAsync($"/public/registrations/{token}")).Content.ReadAsStringAsync();

        Assert.DoesNotContain(nachname, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.invalid", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2345678", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_Selbstmeldung_entsteht_und_nennt_ihren_Bestaetigungscode()
    {
        var (leitung, tournamentId, token, anonym) = await OffenAsync();

        var response = await anonym.PostAsJsonAsync(
            $"/public/registrations/{token}", Meldung(Nachname()), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SelfRegistrationResult>(Json);
        Assert.NotNull(result);
        Assert.Equal(EntryStatus.Applied, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.ConfirmationCode));

        // Und sie ist tatsächlich angekommen — mit ihrer Herkunft im Klartext.
        var entries = await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json);

        var entry = Assert.Single(entries!);
        Assert.Equal(EntryOrigin.SelfService, entry.Origin);
        Assert.Equal(result.ConfirmationCode, entry.ConfirmationCode);
    }

    [Fact]
    public async Task Zweimal_absenden_legt_nur_eine_Meldung_an()
    {
        // Der Doppelklick auf „Absenden" ist der häufigste Fall. Idempotent
        // statt Fehler erschlägt ihn und die E-Mail-Enumeration in einem: wer
        // eine fremde Adresse einträgt, erfährt nicht, ob sie schon gemeldet war.
        var (leitung, tournamentId, token, anonym) = await OffenAsync();
        var meldung = Meldung(Nachname());

        var first = await (await anonym.PostAsJsonAsync($"/public/registrations/{token}", meldung, Json))
            .Content.ReadFromJsonAsync<SelfRegistrationResult>(Json);
        var second = await (await anonym.PostAsJsonAsync($"/public/registrations/{token}", meldung, Json))
            .Content.ReadFromJsonAsync<SelfRegistrationResult>(Json);

        Assert.Equal(first!.ConfirmationCode, second!.ConfirmationCode);

        var entries = await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json);
        Assert.Single(entries!);
    }

    [Fact]
    public async Task Ein_Doppel_meldet_sich_zu_zweit()
    {
        var (leitung, tournamentId, token, anonym) = await OffenAsync(Discipline.Doubles);

        var view = await anonym.GetFromJsonAsync<PublicRegistrationView>(
            $"/public/registrations/{token}", Json);
        Assert.True(view!.NeedsPartner);

        var response = await anonym.PostAsJsonAsync(
            $"/public/registrations/{token}",
            Meldung(Nachname(), Nachname(), "Die Netzroller"),
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entries = await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json);

        var entry = Assert.Single(entries!);
        Assert.StartsWith("Die Netzroller", entry.ParticipantName, StringComparison.Ordinal);
        Assert.Equal(2, entry.Contacts.Count);
    }

    [Fact]
    public async Task Ein_Doppelturnier_weist_eine_Meldung_ohne_Partner_ab()
    {
        // Kein 404, sondern ein benannter Fehler: das liegt am Formular und
        // nicht am Link. Ein 404 wäre für den Melder nicht erklärbar.
        var (_, _, token, anonym) = await OffenAsync(Discipline.Doubles);

        var response = await anonym.PostAsJsonAsync(
            $"/public/registrations/{token}", Meldung(Nachname()), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Partner", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_unbekanntes_Token_und_eine_geschlossene_Meldung_sind_nicht_zu_unterscheiden()
    {
        // Sonst wäre der Endpunkt ein Orakel dafür, welche Token es gibt.
        var (leitung, tournamentId, token, anonym) = await OffenAsync();
        await leitung.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);

        var geschlossen = await anonym.PostAsJsonAsync(
            $"/public/registrations/{token}", Meldung(Nachname()), Json);

        var unbekannt = await anonym.PostAsJsonAsync(
            "/public/registrations/AAAAAAAAAAAAAAAAAAAAAA", Meldung(Nachname()), Json);

        Assert.Equal(HttpStatusCode.NotFound, geschlossen.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);

        // Verglichen wird die Auskunft, nicht der ganze Rumpf: die traceId ist
        // je Anfrage verschieden und sagt über die Ressource nichts.
        Assert.Equal(await DetailAsync(geschlossen), await DetailAsync(unbekannt));
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Json))
        .GetProperty("detail").GetString();

    [Fact]
    public async Task Der_Token_steht_in_keiner_Fehlermeldung()
    {
        // Er ist der Schlüssel zum Melden. Stünde er in ProblemDetails, stünde
        // er in jedem Protokoll, das Antworten mitschreibt (Risiko 5).
        var (leitung, tournamentId, token, anonym) = await OffenAsync();
        await leitung.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);

        var response = await anonym.PostAsJsonAsync(
            $"/public/registrations/{token}", Meldung(Nachname()), Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_geschlossene_Meldung_bleibt_lesbar_und_sagt_dass_sie_zu_ist()
    {
        // Der Kopf bleibt sichtbar — er sagt nicht mehr als ein Aushang, und ein
        // Melder, der auf einen alten Link klickt, soll erfahren, warum nichts
        // geht, statt vor einem 404 zu stehen. Geschrieben wird trotzdem nicht.
        var (leitung, tournamentId, token, anonym) = await OffenAsync();
        await leitung.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);

        var view = await anonym.GetFromJsonAsync<PublicRegistrationView>(
            $"/public/registrations/{token}", Json);

        Assert.False(view!.IsOpen);
        Assert.Equal("Clubmeisterschaft", view.TournamentName);
    }

    [Fact]
    public async Task Nach_dem_Meldeschluss_geht_nichts_mehr()
    {
        _factory.Clock.Now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var (_, _, token, anonym) = await OffenAsync(
            meldeschluss: new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            HttpStatusCode.OK,
            (await anonym.PostAsJsonAsync($"/public/registrations/{token}", Meldung(Nachname()), Json))
                .StatusCode);

        _factory.Clock.Now = new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero);

        var zuSpaet = await anonym.PostAsJsonAsync(
            $"/public/registrations/{token}", Meldung(Nachname()), Json);

        Assert.Equal(HttpStatusCode.NotFound, zuSpaet.StatusCode);
        Assert.False((await anonym.GetFromJsonAsync<PublicRegistrationView>(
            $"/public/registrations/{token}", Json))!.IsOpen);
    }

    [Fact]
    public async Task Ein_volles_Feld_nimmt_weiter_an_aber_auf_die_Warteliste()
    {
        // Abweisen wäre für den Melder die schlechtere Antwort — und die
        // Turnierleitung entscheidet ohnehin, wer nachrückt.
        var (leitung, tournamentId, token, anonym) = await OffenAsync(kapazitaet: 1);

        var erste = await (await anonym.PostAsJsonAsync(
            $"/public/registrations/{token}", Meldung(Nachname()), Json))
            .Content.ReadFromJsonAsync<SelfRegistrationResult>(Json);

        var frei = await anonym.GetFromJsonAsync<PublicRegistrationView>(
            $"/public/registrations/{token}", Json);

        var zweite = await (await anonym.PostAsJsonAsync(
            $"/public/registrations/{token}", Meldung(Nachname()), Json))
            .Content.ReadFromJsonAsync<SelfRegistrationResult>(Json);

        Assert.Equal(EntryStatus.Applied, erste!.Status);
        Assert.Equal(0, frei!.FreeSlots);
        Assert.Equal(EntryStatus.WaitingList, zweite!.Status);

        var entries = await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json);
        Assert.Equal(2, entries!.Count);
    }

    [Fact]
    public async Task Die_Turnierleitung_nimmt_eine_Selbstmeldung_ueber_die_bestehenden_Endpunkte_an()
    {
        // Keine zweite Sorte Meldung: was über den Link kommt, wird genauso
        // angenommen wie eine erfasste.
        var (leitung, tournamentId, token, anonym) = await OffenAsync();

        await anonym.PostAsJsonAsync($"/public/registrations/{token}", Meldung(Nachname()), Json);

        var entryId = (await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json))!.Single().Id;

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await leitung.PostAsync(
                $"/api/tournaments/{tournamentId}/entries/{entryId}/accept", null)).StatusCode);

        var detail = await leitung.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);

        Assert.Equal(EntryStatus.Accepted, detail!.Entries.Single().Status);
    }

    [Fact]
    public async Task Derselbe_Mensch_bekommt_in_zwei_Turnieren_denselben_Spieler()
    {
        // Ohne die Zusammenführung legte er bei jedem Turnier einen neuen an,
        // und die Spielertabelle wüchse mit jeder Ausschreibung (Risiko 6).
        var nachname = Nachname();
        var erstes = await OffenAsync();
        var zweites = await OffenAsync();

        await erstes.Anonym.PostAsJsonAsync(
            $"/public/registrations/{erstes.Token}", Meldung(nachname), Json);
        await zweites.Anonym.PostAsJsonAsync(
            $"/public/registrations/{zweites.Token}", Meldung(nachname), Json);

        var hier = await erstes.Leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{erstes.TournamentId}/entries", Json);
        var dort = await zweites.Leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{zweites.TournamentId}/entries", Json);

        Assert.Equal(
            hier!.Single().Contacts.Single().PlayerId,
            dort!.Single().Contacts.Single().PlayerId);
    }

    [Fact]
    public async Task Der_Anmeldelink_steht_in_keiner_oeffentlichen_Ansicht()
    {
        var (leitung, tournamentId, token, anonym) = await OffenAsync();

        await anonym.PostAsJsonAsync($"/public/registrations/{token}", Meldung(Nachname()), Json);
        await leitung.PostAsync($"/api/tournaments/{tournamentId}/public-view/rebuild", null);

        var raw = await (await anonym.GetAsync($"/public/tournaments/{tournamentId}")).Content
            .ReadAsStringAsync();

        Assert.DoesNotContain(token, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("registrationToken", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ein_erneuertes_Token_macht_das_alte_wertlos()
    {
        var (leitung, tournamentId, token, anonym) = await OffenAsync();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await leitung.PostAsync(
                $"/api/tournaments/{tournamentId}/registration/link/rotate", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await anonym.GetAsync($"/public/registrations/{token}")).StatusCode);

        var neu = await leitung.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{tournamentId}/registration", Json);

        Assert.NotEqual(token, neu!.Token);
        Assert.Equal(HttpStatusCode.OK, (await anonym.GetAsync($"/public/registrations/{neu.Token}")).StatusCode);
    }

    [Fact]
    public async Task Der_Anmeldelink_gehoert_nicht_jedem()
    {
        // Er ist der Schlüssel zum Melden. Wer das Turnier nicht führt, bekommt
        // ihn nicht — und zwar als 404, nicht als 403 (ADR-0004).
        var (_, tournamentId, _, _) = await OffenAsync();

        var fremder = _factory.CreateClientAs($"fremder-{Guid.NewGuid():N}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fremder.GetAsync($"/api/tournaments/{tournamentId}/registration")).StatusCode);
    }

    [Fact]
    public async Task Die_Antwort_traegt_eine_Referrer_Policy()
    {
        // Der Token steht in der Adresszeile des Melders. Ohne diese Kopfzeile
        // stünde er beim nächsten ausgehenden Link im Referer und damit im
        // Protokoll eines fremden Servers.
        var (_, _, token, anonym) = await OffenAsync();

        var response = await anonym.GetAsync($"/public/registrations/{token}");

        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }
}

/// <summary>
/// Die Ratenbegrenzung, mit einer Fabrik, die sie klein stellt.
///
/// Eigene Klasse mit eigener Fabrik: eine geteilte Schranke träfe jeden anderen
/// Test dieser Baugruppe, sobald einer ein paar Meldungen mehr schickt — und
/// zwar an wechselnder Stelle.
/// </summary>
public sealed class AnmeldungRatenbegrenzungTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Zu_viele_Anfragen_werden_abgewiesen()
    {
        // Der Melder ohne Konto ist der einzige, dem sich nichts entziehen
        // lässt: er hat keines. Deshalb genau hier eine Schranke — und deshalb
        // keine anderswo, wo sie am Turniertag den Betrieb träfe.
        using var factory = new TennisTurnierApiFactory([], publicRegistrationLimit: 3);

        var aufbau = await factory.NeuesTurnierAsync(
            "raten-leitung",
            new TurnierWunsch { Anlage = "TC Ratenlimit", Auslosen = false });

        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/registration/open", null);

        var link = await aufbau.Admin.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{aufbau.TournamentId}/registration", Json);

        var anonym = factory.CreateClient();
        var stati = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++)
        {
            stati.Add((await anonym.GetAsync($"/public/registrations/{link!.Token}")).StatusCode);
        }

        Assert.Equal(3, stati.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(2, stati.Count(status => status == HttpStatusCode.TooManyRequests));

        // Die angemeldete Seite bleibt unberührt: die Schranke hängt an den
        // anonymen Endpunkten und nicht an der Turnierleitung.
        Assert.Equal(
            HttpStatusCode.OK,
            (await aufbau.Admin.GetAsync($"/api/tournaments/{aufbau.TournamentId}/registration")).StatusCode);
    }
}
