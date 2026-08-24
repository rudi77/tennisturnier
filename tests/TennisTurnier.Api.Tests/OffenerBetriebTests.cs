using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
}
