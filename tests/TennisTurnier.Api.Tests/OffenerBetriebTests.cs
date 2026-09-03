using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TennisTurnier.Application.Membership;
using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die Instanz ohne Anmeldung.
///
/// Der erste Schritt: eine Instanz steht, bevor ein Identity Provider steht.
/// Dann gilt jeder Aufruf als derselbe Benutzer, und der darf alles. Was hier
/// geprüft wird, ist deshalb nicht nur „es geht", sondern auch, dass es genau
/// ein Konto bleibt — sonst gehörte jedes Turnier einem anderen Aufruf und wäre
/// beim nächsten nicht mehr da.
/// </summary>
public sealed class OffenerBetriebTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Ohne_Anmeldung_darf_man_alles()
    {
        using var fabrik = new TennisTurnierApiFactory([], openAccess: true);

        // Kein Header, kein Token — genau so, wie ein Browser ohne Anmeldung
        // anfragt.
        var client = fabrik.CreateClient();

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);
        Assert.NotNull(me);
        Assert.True(me.IsSystemAdmin);
        Assert.Equal("Ohne Anmeldung", me.DisplayName);

        var vorlagen = await client.GetFromJsonAsync<List<FormatTemplateSummary>>(
            "/api/format-templates", Json);

        var angelegt = await client.PostAsJsonAsync(
            "/api/tournaments",
            new CreateTournamentRequest(
                "Clubmeisterschaft ohne Anmeldung",
                "TC Maria Alm",
                null,
                "Maria Alm",
                "Europe/Vienna",
                Discipline.Singles,
                new DateOnly(2026, 6, 6),
                new DateOnly(2026, 6, 6),
                vorlagen!.Single(v => v.Name == BuiltInFormats.Knockout.Name).Id),
            Json);

        Assert.Equal(HttpStatusCode.Created, angelegt.StatusCode);
    }

    [Fact]
    public async Task Es_bleibt_bei_einem_Konto()
    {
        // Der Punkt, an dem eine naive Fassung scheitert: legte jeder Aufruf
        // seinen eigenen Benutzer an, wäre das eben angelegte Turnier beim
        // nächsten Aufruf nicht mehr sichtbar — der Query-Filter aus ADR-0004
        // zeigt nur, was dem Aufrufer gehört.
        using var fabrik = new TennisTurnierApiFactory([], openAccess: true);

        var erster = fabrik.CreateClient();
        var zweiter = fabrik.CreateClient();

        var vorlagen = await erster.GetFromJsonAsync<List<FormatTemplateSummary>>(
            "/api/format-templates", Json);

        var angelegt = await erster.PostAsJsonAsync(
            "/api/tournaments",
            new CreateTournamentRequest(
                "Nachmittagsturnier",
                "TC Maria Alm",
                null,
                "Maria Alm",
                "Europe/Vienna",
                Discipline.Singles,
                new DateOnly(2026, 6, 7),
                new DateOnly(2026, 6, 7),
                vorlagen!.Single(v => v.Name == BuiltInFormats.Knockout.Name).Id),
            Json);

        Assert.Equal(HttpStatusCode.Created, angelegt.StatusCode);

        var meins = await erster.GetFromJsonAsync<MeResponse>("/api/me", Json);
        var seins = await zweiter.GetFromJsonAsync<MeResponse>("/api/me", Json);
        Assert.Equal(meins!.UserId, seins!.UserId);

        // Und die Rolle wird nicht bei jedem Aufruf noch einmal vergeben —
        // daneben steht nur, was das Anlegen des Turniers mit sich brachte.
        Assert.Single(seins.Roles, r => r.Role == Role.SystemAdmin);

        var seineTurniere = await zweiter.GetFromJsonAsync<List<TournamentSummary>>(
            "/api/tournaments", Json);

        Assert.Contains(seineTurniere!, t => t.Name == "Nachmittagsturnier");
    }

    [Fact]
    public async Task Der_Beitrittslink_traegt_auch_ohne_Anmeldeverfahren()
    {
        // Der Fehler, den eine ausgelieferte Instanz gezeigt hat: `/api/join`
        // steht hinter `RequireAuthorization`, und ohne Aussteller gibt es kein
        // Verfahren, mit dem sich jemand ausweisen könnte. Die Autorisierung
        // forderte trotzdem einen Ausweis an, fand niemanden, der ihn ausstellt,
        // und der Aufruf endete mit „No authenticationScheme was specified" —
        // einer 500 auf den Weg, der offenstehen sollte. Jeder geteilte Link war
        // damit tot.
        //
        // `testSchema: false` ist hier der Kern des Tests und keine Beiläufigkeit:
        // mit dem Testschema gibt es ein Verfahren, die Autorisierung antwortete
        // sauber mit 401 statt zu werfen, und der Fehler bliebe unsichtbar —
        // genau deshalb ist er durch alle bestehenden Läufe gekommen.
        using var fabrik = new TennisTurnierApiFactory([], openAccess: true, testSchema: false);
        var client = fabrik.CreateClient();

        var vorlagen = await client.GetFromJsonAsync<List<FormatTemplateSummary>>(
            "/api/format-templates", Json);

        var angelegt = await client.PostAsJsonAsync(
            "/api/tournaments",
            new CreateTournamentRequest(
                "Turnier mit geteiltem Link",
                "TC Offen",
                null,
                "Maria Alm",
                "Europe/Vienna",
                Discipline.Singles,
                new DateOnly(2026, 6, 6),
                new DateOnly(2026, 6, 6),
                vorlagen!.Single(v => v.Name == BuiltInFormats.Knockout.Name).Id),
            Json);

        var turnier = (await angelegt.Content.ReadFromJsonAsync<TournamentDetail>(Json))!;

        await client.PostAsync($"/api/tournaments/{turnier.Id}/registration/open", null);

        var link = await client.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{turnier.Id}/registration", Json);

        var ansicht = await client.GetAsync($"/api/join/{link!.Token}");

        Assert.Equal(HttpStatusCode.OK, ansicht.StatusCode);

        var beitritt = (await ansicht.Content.ReadFromJsonAsync<JoinView>(Json))!;

        Assert.Equal(turnier.Id, beitritt.TournamentId);
        Assert.True(beitritt.IsOpen);

        // Und dazu gehört er schon: im offenen Betrieb gibt es einen Benutzer,
        // und der hat das Turnier angelegt.
        Assert.True(beitritt.AlreadyMember);
    }

    [Fact]
    public async Task Auch_der_Beitritt_selbst_geht_ohne_Anmeldeverfahren()
    {
        // Nicht nur die Auskunft, auch die Handlung: `POST` steht hinter
        // derselben Sperre.
        using var fabrik = new TennisTurnierApiFactory([], openAccess: true, testSchema: false);
        var client = fabrik.CreateClient();

        var vorlagen = await client.GetFromJsonAsync<List<FormatTemplateSummary>>(
            "/api/format-templates", Json);

        var angelegt = await client.PostAsJsonAsync(
            "/api/tournaments",
            new CreateTournamentRequest(
                "Turnier zum Beitreten",
                "TC Offen",
                null,
                "Maria Alm",
                "Europe/Vienna",
                Discipline.Singles,
                new DateOnly(2026, 6, 6),
                new DateOnly(2026, 6, 6),
                vorlagen!.Single(v => v.Name == BuiltInFormats.Knockout.Name).Id),
            Json);

        var turnier = (await angelegt.Content.ReadFromJsonAsync<TournamentDetail>(Json))!;

        await client.PostAsync($"/api/tournaments/{turnier.Id}/registration/open", null);

        var link = await client.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{turnier.Id}/registration", Json);

        var antwort = await client.PostAsJsonAsync(
            $"/api/join/{link!.Token}",
            new JoinRequest(true, "Anna", "Müller", null, null, null, null, null),
            Json);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);

        var ergebnis = (await antwort.Content.ReadFromJsonAsync<JoinResult>(Json))!;

        Assert.Equal(turnier.Id, ergebnis.TournamentId);
        Assert.NotNull(ergebnis.EntryId);
    }

    [Fact]
    public async Task Die_Oberflaeche_erfaehrt_davon()
    {
        // Ohne diese Angabe stünde die Oberfläche vor einer Anmeldemaske, hinter
        // der es nichts anzumelden gibt.
        using var fabrik = new TennisTurnierApiFactory([], openAccess: true);

        var skript = await fabrik.CreateClient().GetStringAsync("/config.js");

        Assert.Contains("\"openAccess\":true", skript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_offenen_Betrieb_bleibt_der_Aufruf_anonym()
    {
        // Die Gegenprobe: der Schalter wirkt, weil er gesetzt ist, und nicht,
        // weil keine Authority konfiguriert ist.
        using var fabrik = new TennisTurnierApiFactory();

        var response = await fabrik.CreateClient().GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// Anmeldung und offener Betrieb zusammen: die Anwendung startet nicht.
    ///
    /// Der stille Ausgang wäre der gefährliche — ein versehentlich gesetzter
    /// Schalter machte eine angemeldete Instanz auf, ohne dass jemand es merkt.
    /// </summary>
    private sealed class WidersprüchlicheFabrik : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:AutoMigrate", "false");
            builder.UseSetting("Oidc:Authority", "https://idp.example.invalid/realms/matchday");
            builder.UseSetting("Security:OpenAccess", "true");
        }
    }

    [Fact]
    public void Anmeldung_und_offener_Betrieb_schliessen_einander_aus()
    {
        using var fabrik = new WidersprüchlicheFabrik();

        var fehler = Assert.Throws<InvalidOperationException>(() => fabrik.CreateClient());

        Assert.Contains("Security:OpenAccess", fehler.Message, StringComparison.Ordinal);
        Assert.Contains("Oidc:Authority", fehler.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ein Aussteller ohne erwarteten Empfänger: die Anwendung startet nicht.
    ///
    /// Eine leere Audience schaltete die Prüfung stillschweigend ab, und dann
    /// gilt jedes Token dieses Ausstellers — auch eines, das für einen ganz
    /// anderen Client seines Verbunds ausgestellt wurde. Wer das braucht, sagt
    /// es über <c>Oidc:RequireAudience</c>.
    /// </summary>
    private sealed class FabrikOhneEmpfaenger : WebApplicationFactory<Program>
    {
        internal bool Ausdruecklich { get; init; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:AutoMigrate", "false");
            builder.UseSetting("Oidc:Authority", "https://idp.example.invalid/realms/matchday");
            builder.UseSetting("Oidc:Audience", string.Empty);

            if (Ausdruecklich)
            {
                builder.UseSetting("Oidc:RequireAudience", "false");
            }
        }
    }

    [Fact]
    public void Ein_Aussteller_ohne_erwarteten_Empfaenger_haelt_die_Anwendung_an()
    {
        using var fabrik = new FabrikOhneEmpfaenger();

        var fehler = Assert.Throws<InvalidOperationException>(() => fabrik.CreateClient());

        Assert.Contains("Oidc:Audience", fehler.Message, StringComparison.Ordinal);
        Assert.Contains("Oidc:RequireAudience", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wer_es_ausdruecklich_sagt_darf_ohne_Empfaenger_starten()
    {
        using var fabrik = new FabrikOhneEmpfaenger { Ausdruecklich = true };

        // Kein Wurf: die Anwendung fährt hoch, und die öffentliche Ansicht
        // steht — geprüft wird der Empfänger dann eben nicht.
        Assert.NotNull(fabrik.CreateClient());
    }
}
