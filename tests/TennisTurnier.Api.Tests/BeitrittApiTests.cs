using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Membership;
using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der Beitritt über den geteilten Link.
///
/// Er ersetzt die anonyme Selbstmeldung (ADR-0012 löst ADR-0010 ab). Der Link
/// bleibt die Eintrittskarte — wer ihn hat, darf herein, ohne dass ihn jemand
/// einzeln freischaltet. Was sich ändert: er verlangt ein Konto, und wer
/// beitritt, gehört danach dazu, statt einen Bestätigungscode mitzunehmen.
///
/// Autorisiert ist der Weg weiterhin allein durch das Token im Pfad: der
/// Beitretende hat noch keine Rolle am Turnier, und der Query-Filter aus
/// ADR-0004 blendet es für ihn aus. Was hier schiefgeht, geht still schief —
/// die Meldung wäre gespeichert und der Beitretende bekäme 404.
/// </summary>
public sealed class BeitrittApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public BeitrittApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>Ein Turnier mit offener Meldung samt seinem Beitrittslink.</summary>
    private async Task<(HttpClient Leitung, Guid TournamentId, string Token)> OffenAsync(
        Discipline disziplin = Discipline.Singles,
        int? kapazitaet = null,
        DateTimeOffset? meldeschluss = null)
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"beitritt-leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Anlage = "TC Beitritt", Disziplin = disziplin, Auslosen = false });

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

        return (aufbau.Admin, aufbau.TournamentId, link!.Token);
    }

    /// <summary>
    /// Jemand mit Konto, der den Link bekommen hat — und der Name, unter dem er
    /// mitspielen würde. Die E-Mail steht am Konto und nicht im Formular.
    /// </summary>
    private (HttpClient Client, string Nachname) Interessent()
    {
        var nachname = $"Beitritt{Guid.NewGuid():N}"[..16];
        var client = _factory.CreateClientAs(
            $"beitritt-{Guid.NewGuid():N}", $"{nachname.ToLowerInvariant()}@example.invalid");

        return (client, nachname);
    }

    private static JoinRequest Mitspielen(
        string nachname,
        string? partnerNachname = null,
        string? teamName = null) =>
        new(
            Play: true,
            "Anna",
            nachname,
            "+43 1 2345678",
            partnerNachname is null ? null : "Eva",
            partnerNachname,
            partnerNachname is null ? null : $"{partnerNachname.ToLowerInvariant()}@example.invalid",
            teamName);

    private static readonly JoinRequest NurZusehen =
        new(Play: false, null, null, null, null, null, null, null);

    [Fact]
    public async Task Ohne_Anmeldung_geht_hier_gar_nichts()
    {
        // Der Kern des Umbaus: der Link führt zur Anmeldung und nicht an ihr
        // vorbei. Auch die Auskunft bleibt zu — angemeldet ist noch nicht
        // dabei, aber unangemeldet ist gar nichts.
        var (_, _, token) = await OffenAsync();
        var anonym = _factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonym.GetAsync($"/api/join/{token}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonym.PostAsJsonAsync($"/api/join/{token}", NurZusehen, Json)).StatusCode);
    }

    [Fact]
    public async Task Wer_dem_Link_folgt_sieht_den_Turnierkopf()
    {
        var (_, tournamentId, token) = await OffenAsync();
        var (client, _) = Interessent();

        var view = await client.GetFromJsonAsync<JoinView>($"/api/join/{token}", Json);

        Assert.NotNull(view);
        Assert.Equal(tournamentId, view.TournamentId);
        Assert.Equal("Clubmeisterschaft", view.TournamentName);
        Assert.Equal("TC Beitritt", view.VenueName);
        Assert.Equal(Discipline.Singles, view.Discipline);
        Assert.False(view.NeedsPartner);
        Assert.True(view.IsOpen);
        Assert.Null(view.FreeSlots);
        Assert.False(view.AlreadyMember);
    }

    [Fact]
    public async Task Die_Auskunft_am_Link_nennt_keine_Namen()
    {
        // Der Link darf kein Weg an der Projektion vorbei sein. Steht dort erst
        // eine Teilnehmerliste, ist die Datensparsamkeit von ADR-0003 umgangen
        // — und zwar von der Seite, die niemand prüft. Dass der Aufrufer ein
        // Konto hat, ändert daran nichts: er gehört noch nicht dazu.
        var (_, _, token) = await OffenAsync();
        var (erster, nachname) = Interessent();
        await erster.PostAsJsonAsync($"/api/join/{token}", Mitspielen(nachname), Json);

        var (zweiter, _) = Interessent();
        var raw = await (await zweiter.GetAsync($"/api/join/{token}")).Content.ReadAsStringAsync();

        Assert.DoesNotContain(nachname, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.invalid", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2345678", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_Beitritt_macht_zum_Mitglied_und_meldet_zugleich()
    {
        var (leitung, tournamentId, token) = await OffenAsync();
        var (client, nachname) = Interessent();

        var response = await client.PostAsJsonAsync(
            $"/api/join/{token}", Mitspielen(nachname), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JoinResult>(Json);
        Assert.NotNull(result);
        Assert.Equal(tournamentId, result.TournamentId);
        Assert.Equal(EntryStatus.Applied, result.Status);
        Assert.NotNull(result.EntryId);

        // Die Meldung ist angekommen — mit ihrer Herkunft im Klartext.
        var entries = await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json);

        var entry = Assert.Single(entries!);
        Assert.Equal(EntryOrigin.SelfService, entry.Origin);

        // Und er gehört jetzt dazu: das Turnier steht unter seinen eigenen.
        var meine = await client.GetFromJsonAsync<List<TournamentSummary>>("/api/tournaments", Json);
        Assert.Contains(meine!, t => t.Id == tournamentId);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);
        Assert.Contains(me!.Roles, r => r.Role == Role.Member && r.ResourceId == tournamentId);
    }

    [Fact]
    public async Task Man_kann_auch_beitreten_ohne_mitzuspielen()
    {
        // Genau dafür ist ein Turnier eine Gruppe: der Partner ohne eigene
        // Meldung, der Vereinskollege, der nur den Spielplan sehen will.
        var (leitung, tournamentId, token) = await OffenAsync();
        var (client, _) = Interessent();

        var result = await (await client.PostAsJsonAsync($"/api/join/{token}", NurZusehen, Json))
            .Content.ReadFromJsonAsync<JoinResult>(Json);

        Assert.NotNull(result);
        Assert.Null(result.EntryId);
        Assert.Null(result.Status);

        var meine = await client.GetFromJsonAsync<List<TournamentSummary>>("/api/tournaments", Json);
        Assert.Contains(meine!, t => t.Id == tournamentId);

        var entries = await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json);
        Assert.Empty(entries!);
    }

    [Fact]
    public async Task Nach_dem_Meldeschluss_wird_man_noch_Mitglied_aber_meldet_nicht_mehr()
    {
        // Der Meldeschluss beendet die Teilnehmerliste, nicht die
        // Zugehörigkeit. Wer danach dazukommt, sieht den Spielplan.
        var (leitung, tournamentId, token) = await OffenAsync();
        await leitung.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);

        var (client, nachname) = Interessent();
        var result = await (await client.PostAsJsonAsync(
            $"/api/join/{token}", Mitspielen(nachname), Json))
            .Content.ReadFromJsonAsync<JoinResult>(Json);

        Assert.Null(result!.EntryId);

        var meine = await client.GetFromJsonAsync<List<TournamentSummary>>("/api/tournaments", Json);
        Assert.Contains(meine!, t => t.Id == tournamentId);
    }

    [Fact]
    public async Task Zweimal_beitreten_legt_nur_eine_Meldung_und_eine_Rolle_an()
    {
        // Der Doppelklick auf „Absenden" ist der häufigste Fall — und derselbe
        // Link ein zweites Mal ist kein zweiter Beitritt.
        var (leitung, tournamentId, token) = await OffenAsync();
        var (client, nachname) = Interessent();
        var beitritt = Mitspielen(nachname);

        var first = await (await client.PostAsJsonAsync($"/api/join/{token}", beitritt, Json))
            .Content.ReadFromJsonAsync<JoinResult>(Json);
        var second = await (await client.PostAsJsonAsync($"/api/join/{token}", beitritt, Json))
            .Content.ReadFromJsonAsync<JoinResult>(Json);

        Assert.Equal(first!.EntryId, second!.EntryId);

        var entries = await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json);
        Assert.Single(entries!);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);
        Assert.Single(me!.Roles, r => r.Role == Role.Member && r.ResourceId == tournamentId);

        // Und beim zweiten Aufruf weiß die Auskunft, dass er schon dabei ist.
        var view = await client.GetFromJsonAsync<JoinView>($"/api/join/{token}", Json);
        Assert.True(view!.AlreadyMember);
    }

    [Fact]
    public async Task Die_Turnierleitung_bleibt_bei_ihrer_eigenen_Rolle()
    {
        // Sie ist kein Mitglied zweiter Klasse — und eine zweite Rolle am
        // selben Turnier sagte nichts, was die erste nicht schon sagt.
        var (leitung, tournamentId, token) = await OffenAsync();

        Assert.Equal(
            HttpStatusCode.OK,
            (await leitung.PostAsJsonAsync($"/api/join/{token}", NurZusehen, Json)).StatusCode);

        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        Assert.DoesNotContain(rollen!, r => r.Role == Role.Member);
    }

    [Fact]
    public async Task Ein_zweites_Turnier_findet_denselben_Spieler_wieder()
    {
        // Die Verbindung zwischen Konto und Spieler: wer schon einmal
        // mitgespielt hat, ist derselbe Spieler und nicht ein zweiter mit
        // gleichem Namen — auch wenn er beim zweiten Mal einen anderen eintippt.
        var (ersteLeitung, erstesTurnier, erstesToken) = await OffenAsync();
        var (zweiteLeitung, zweitesTurnier, zweitesToken) = await OffenAsync();
        var (client, nachname) = Interessent();

        await client.PostAsJsonAsync($"/api/join/{erstesToken}", Mitspielen(nachname), Json);
        await client.PostAsJsonAsync($"/api/join/{zweitesToken}", Mitspielen("Andersgeschrieben"), Json);

        var ersteMeldungen = await ersteLeitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{erstesTurnier}/entries", Json);

        // Über die Leitung des zweiten Turniers: ein Mitglied sieht sein
        // Turnier, aber nicht die Innenansicht mit den Kontaktdaten.
        var zweiteMeldungen = await zweiteLeitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{zweitesTurnier}/entries", Json);

        // Beide Male derselbe Anzeigename: der Spieler kommt aus dem Konto und
        // nicht aus dem Formular.
        Assert.Equal(
            Assert.Single(ersteMeldungen!).ParticipantName,
            Assert.Single(zweiteMeldungen!).ParticipantName);
    }

    [Fact]
    public async Task Ein_Doppel_nennt_seinen_Partner()
    {
        var (leitung, tournamentId, token) = await OffenAsync(Discipline.Doubles);
        var (client, nachname) = Interessent();

        var view = await client.GetFromJsonAsync<JoinView>($"/api/join/{token}", Json);
        Assert.True(view!.NeedsPartner);

        var response = await client.PostAsJsonAsync(
            $"/api/join/{token}",
            Mitspielen(nachname, "Netzroller", "Die Netzroller"),
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
        // nicht am Link. Ein 404 wäre für den Beitretenden nicht erklärbar.
        var (_, _, token) = await OffenAsync(Discipline.Doubles);
        var (client, nachname) = Interessent();

        var response = await client.PostAsJsonAsync(
            $"/api/join/{token}", Mitspielen(nachname), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Partner", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_abgewiesene_Meldung_macht_niemanden_zum_Mitglied()
    {
        // Sonst gehörte man einer Gruppe an, weil man sich vertippt hat.
        var (_, tournamentId, token) = await OffenAsync(Discipline.Doubles);
        var (client, nachname) = Interessent();

        await client.PostAsJsonAsync($"/api/join/{token}", Mitspielen(nachname), Json);

        var meine = await client.GetFromJsonAsync<List<TournamentSummary>>("/api/tournaments", Json);
        Assert.DoesNotContain(meine!, t => t.Id == tournamentId);
    }

    [Fact]
    public async Task Zum_Mitspielen_braucht_es_einen_Namen()
    {
        var (_, _, token) = await OffenAsync();
        var (client, _) = Interessent();

        var response = await client.PostAsJsonAsync(
            $"/api/join/{token}",
            new JoinRequest(Play: true, null, null, null, null, null, null, null),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Nachname", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_unbekanntes_Token_und_ein_leeres_sind_nicht_zu_unterscheiden()
    {
        // Sonst wäre der Endpunkt ein Orakel dafür, welche Token es gibt.
        var (client, _) = Interessent();

        var unbekannt = await client.PostAsJsonAsync(
            "/api/join/AAAAAAAAAAAAAAAAAAAAAA", NurZusehen, Json);

        var leer = await client.GetAsync("/api/join/%20%20");

        Assert.Equal(HttpStatusCode.NotFound, unbekannt.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, leer.StatusCode);

        // Verglichen wird die Auskunft, nicht der ganze Rumpf: die traceId ist
        // je Anfrage verschieden und sagt über die Ressource nichts.
        Assert.Equal(await DetailAsync(unbekannt), await DetailAsync(leer));
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Json))
        .GetProperty("detail").GetString();

    [Fact]
    public async Task Der_Token_steht_in_keiner_Fehlermeldung()
    {
        // Er ist der Schlüssel zum Beitreten. Stünde er in ProblemDetails,
        // stünde er in jedem Protokoll, das Antworten mitschreibt (Risiko 5).
        var (_, _, token) = await OffenAsync(Discipline.Doubles);
        var (client, nachname) = Interessent();

        var response = await client.PostAsJsonAsync(
            $"/api/join/{token}", Mitspielen(nachname), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_geschlossene_Meldung_bleibt_lesbar_und_sagt_dass_sie_zu_ist()
    {
        // Der Kopf bleibt sichtbar — er sagt nicht mehr als ein Aushang, und
        // wer auf einen alten Link klickt, soll erfahren, warum nicht mehr
        // gemeldet werden kann, statt vor einem 404 zu stehen.
        var (leitung, tournamentId, token) = await OffenAsync();
        await leitung.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);

        var (client, _) = Interessent();
        var view = await client.GetFromJsonAsync<JoinView>($"/api/join/{token}", Json);

        Assert.False(view!.IsOpen);
        Assert.Equal("Clubmeisterschaft", view.TournamentName);
    }

    [Fact]
    public async Task Nach_dem_Meldeschluss_ist_die_Meldung_zu()
    {
        _factory.Clock.Now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var (_, _, token) = await OffenAsync(
            meldeschluss: new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero));

        var (rechtzeitig, ersterName) = Interessent();
        Assert.NotNull((await (await rechtzeitig.PostAsJsonAsync(
            $"/api/join/{token}", Mitspielen(ersterName), Json))
            .Content.ReadFromJsonAsync<JoinResult>(Json))!.EntryId);

        _factory.Clock.Now = new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero);

        var (zuSpaet, zweiterName) = Interessent();
        var ergebnis = await (await zuSpaet.PostAsJsonAsync(
            $"/api/join/{token}", Mitspielen(zweiterName), Json))
            .Content.ReadFromJsonAsync<JoinResult>(Json);

        Assert.Null(ergebnis!.EntryId);
        Assert.False((await zuSpaet.GetFromJsonAsync<JoinView>($"/api/join/{token}", Json))!.IsOpen);
    }

    [Fact]
    public async Task Ein_volles_Feld_nimmt_weiter_an_aber_auf_die_Warteliste()
    {
        // Abweisen wäre für den Beitretenden die schlechtere Antwort — und die
        // Turnierleitung entscheidet ohnehin, wer nachrückt.
        var (leitung, tournamentId, token) = await OffenAsync(kapazitaet: 1);

        var (erster, ersterName) = Interessent();
        var erste = await (await erster.PostAsJsonAsync(
            $"/api/join/{token}", Mitspielen(ersterName), Json))
            .Content.ReadFromJsonAsync<JoinResult>(Json);

        var (zweiter, zweiterName) = Interessent();
        var frei = await zweiter.GetFromJsonAsync<JoinView>($"/api/join/{token}", Json);

        var zweite = await (await zweiter.PostAsJsonAsync(
            $"/api/join/{token}", Mitspielen(zweiterName), Json))
            .Content.ReadFromJsonAsync<JoinResult>(Json);

        Assert.Equal(EntryStatus.Applied, erste!.Status);
        Assert.Equal(0, frei!.FreeSlots);
        Assert.Equal(EntryStatus.WaitingList, zweite!.Status);

        var entries = await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json);
        Assert.Equal(2, entries!.Count);
    }

    [Fact]
    public async Task Ein_erneuerter_Link_macht_den_alten_wertlos()
    {
        // Der Weg zurück, wenn ein Link dort gelandet ist, wo er nicht
        // hingehört: er lässt sich nicht zurückholen, aber entwerten.
        var (leitung, tournamentId, alt) = await OffenAsync();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await leitung.PostAsync(
                $"/api/tournaments/{tournamentId}/registration/link/rotate", null)).StatusCode);

        var neu = (await leitung.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{tournamentId}/registration", Json))!.Token;

        Assert.NotEqual(alt, neu);

        var (client, nachname) = Interessent();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/join/{alt}", Mitspielen(nachname), Json)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/join/{neu}", Mitspielen(nachname), Json)).StatusCode);
    }

    [Fact]
    public async Task Die_Turnierleitung_nimmt_einen_Beitritt_ueber_die_bestehenden_Endpunkte_an()
    {
        // Keine zweite Sorte Meldung: was über den Link kommt, wird genauso
        // angenommen wie eine erfasste.
        var (leitung, tournamentId, token) = await OffenAsync();
        var (client, nachname) = Interessent();

        await client.PostAsJsonAsync($"/api/join/{token}", Mitspielen(nachname), Json);

        var entries = await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json);
        var entry = Assert.Single(entries!);

        var response = await leitung.PostAsync(
            $"/api/tournaments/{tournamentId}/entries/{entry.Id}/accept", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
