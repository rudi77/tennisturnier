using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Adapters.Persistence.Sqlite;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.Tournaments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Wie die Anwendung hochfährt.
///
/// Der Rest der Testsuite fährt sie in genau einer Form hoch: Umgebung
/// „Testing", Migration von Hand. Die beiden anderen Formen — die
/// Entwicklungsumgebung mit ihrer Schnittstellenbeschreibung und der Start, der
/// die Datenbank selbst wandert — laufen damit nirgends. Sie sind aber die,
/// mit denen tatsächlich gestartet wird: die eine auf jedem Entwicklungsrechner,
/// die andere in Produktion.
/// </summary>
public sealed class StartArtenTests
{
    /// <summary>
    /// Eine eigene Fabrik: die gemeinsame stellt die Umgebung auf „Testing" und
    /// schaltet die Migration ab. Beides ist hier gerade der Gegenstand.
    /// </summary>
    private sealed class EntwicklungsFabrik : WebApplicationFactory<Program>
    {
        private readonly string _datenbank =
            Path.Combine(Path.GetTempPath(), $"tennisturnier-start-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_datenbank}");
            builder.UseSetting("Oidc:Authority", string.Empty);

            // Ausdrücklich an: dass der Start die Datenbank wandert und die
            // mitgelieferten Vorlagen sät, ist der Weg, den eine frische
            // Instanz geht.
            builder.UseSetting("Database:AutoMigrate", "true");
        }
    }

    /// <summary>
    /// Die Vorlagen über die Dienste gezählt und nicht über den Endpunkt: der
    /// verlangt seit der Fallback-Richtlinie einen angemeldeten Aufrufer, und
    /// diese Fabrik hat kein Verfahren, mit dem sich jemand ausweisen könnte.
    /// Gegenstand ist ohnehin die Saat und nicht der Endpunkt.
    /// </summary>
    private static async Task<int> VorlagenAsync(EntwicklungsFabrik fabrik)
    {
        using var bereich = fabrik.Services.CreateScope();
        var vorlagen = bereich.ServiceProvider.GetRequiredService<IFormatTemplateRepository>();

        return (await vorlagen.ListForCallerAsync()).Count;
    }

    [Fact]
    public async Task Eine_frische_Instanz_wandert_ihre_Datenbank_selbst()
    {
        using var fabrik = new EntwicklungsFabrik();
        var client = fabrik.CreateClient();

        var gesundheit = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, gesundheit.StatusCode);

        // Die Vorlagen stehen nach dem Start bereit — ohne dass ein Test sie
        // gesät hätte.
        Assert.NotEqual(0, await VorlagenAsync(fabrik));
    }

    [Fact]
    public async Task Die_Entwicklungsumgebung_beschreibt_ihre_Schnittstelle()
    {
        // Ohne sie steht ein neuer Mitarbeiter vor einer API ohne Beschreibung —
        // und in Produktion soll sie gerade nicht erreichbar sein.
        using var fabrik = new EntwicklungsFabrik();

        var response = await fabrik.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ein_zweiter_Start_legt_die_Vorlagen_nicht_erneut_an()
    {
        // Jeder Neustart sät. Legte er dabei ein zweites Mal an, stünde nach
        // einem Monat jede Standardvorlage dreißigmal in der Auswahl.
        using var fabrik = new EntwicklungsFabrik();

        var vorher = await VorlagenAsync(fabrik);

        await fabrik.Services.SeedBuiltInFormatsAsync();

        var nachher = await VorlagenAsync(fabrik);

        Assert.NotEqual(0, vorher);
        Assert.Equal(vorher, nachher);
    }

    [Fact]
    public async Task Die_Oberflaeche_bekommt_ihre_Anmeldedaten_zur_Laufzeit()
    {
        // Eine Single-Page-Anwendung mit einkompilierter Authority ließe sich
        // nur für genau einen Aussteller ausliefern — dasselbe Bild wäre in
        // einer zweiten Instanz unbrauchbar.
        using var fabrik = new EntwicklungsFabrik();

        using var client = fabrik
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Oidc:Authority", "https://idp.example.invalid/realms/matchday");
                builder.UseSetting("Oidc:ClientId", "matchday-web");
            })
            .CreateClient();

        var response = await client.GetAsync("/config.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/javascript", response.Content.Headers.ContentType!.MediaType);

        var skript = await response.Content.ReadAsStringAsync();

        Assert.StartsWith("window.__tennisturnier = {", skript, StringComparison.Ordinal);
        Assert.Contains(
            "\"oidcAuthority\":\"https://idp.example.invalid/realms/matchday\"",
            skript,
            StringComparison.Ordinal);
        Assert.Contains("\"oidcClientId\":\"matchday-web\"", skript, StringComparison.Ordinal);
        Assert.Contains("\"oidcScope\":\"openid profile email\"", skript, StringComparison.Ordinal);
    }
}
