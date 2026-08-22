using System.Net;
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

    [Fact]
    public async Task Eine_frische_Instanz_wandert_ihre_Datenbank_selbst()
    {
        using var fabrik = new EntwicklungsFabrik();
        var client = fabrik.CreateClient();

        // Die Vorlagen stehen nach dem Start bereit — ohne dass ein Test sie
        // gesät hätte.
        var vorlagen = await client.GetAsync("/api/format-templates");
        Assert.Equal(HttpStatusCode.OK, vorlagen.StatusCode);

        var gesundheit = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, gesundheit.StatusCode);
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
    public void Ohne_erkennbare_Herkunft_teilen_sich_alle_eine_Schranke()
    {
        var ohne = new DefaultHttpContext();
        Assert.Equal("unbekannt", Program.PartitionKeyOf(ohne));

        var mit = new DefaultHttpContext();
        mit.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");

        Assert.Equal("203.0.113.7", Program.PartitionKeyOf(mit));
    }
}
