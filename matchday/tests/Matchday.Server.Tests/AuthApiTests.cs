using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matchday.Server.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Matchday.Server.Tests;

/// <summary>
/// Die Anmeldung durch den ganzen Stapel: Was abgeriegelt ist, was offen
/// bleibt, und was beim Start passiert, wenn der Schalter halb gesetzt ist.
/// </summary>
public sealed class AuthApiTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"matchday-auth-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _mitAnmeldung;

    public AuthApiTests()
    {
        _mitAnmeldung = Bauen(builder =>
        {
            builder.UseSetting("Auth:Required", "true");
            builder.UseSetting("Auth:GoogleClientId", "matchday.apps.googleusercontent.com");
        });
    }

    public void Dispose()
    {
        _mitAnmeldung.Dispose();
        Aufbau.Wegräumen(_path);
    }

    [Fact]
    public async Task Die_Oberflaeche_erfaehrt_offen_ob_sie_eine_Anmeldung_braucht()
    {
        // Offen mit Absicht: Wer sich anmelden soll, muss vor der Anmeldung
        // lesen können, dass und womit er es tun soll.
        var antwort = await _mitAnmeldung.CreateClient().GetFromJsonAsync<JsonElement>("/api/auth/config");

        Assert.True(antwort.GetProperty("required").GetBoolean());
        Assert.Equal("matchday.apps.googleusercontent.com", antwort.GetProperty("googleClientId").GetString());
    }

    [Fact]
    public async Task Ohne_Anmeldung_kommt_401_und_nicht_403()
    {
        var client = _mitAnmeldung.CreateClient();
        client.DefaultRequestHeaders.Add(Endpoints.ClientHeader, "browser-fremd");

        var antwort = await client.PostAsJsonAsync("/api/tournaments", new { name = "Heimlich" });

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
    }

    [Fact]
    public async Task Auch_die_eigene_Liste_ist_zu()
    {
        var client = _mitAnmeldung.CreateClient();
        client.DefaultRequestHeaders.Add(Endpoints.ClientHeader, "browser-fremd");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tournaments")).StatusCode);
    }

    [Fact]
    public async Task Der_Verwalterlink_laesst_sich_nicht_mehr_ohne_Anmeldung_einloesen()
    {
        // Nicht 404: Es geht nicht darum, ob es das Token gibt, sondern darum,
        // dass ohne Anmeldung niemand danach fragen darf.
        var antwort = await _mitAnmeldung.CreateClient().GetAsync("/api/tournaments/by-admin/irgendein-token");

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
    }

    [Fact]
    public async Task Das_Mitschauen_bleibt_offen()
    {
        // Der Kern von ADR-0016: Zuschauer haben kein Konto und sollen keins
        // brauchen. 404 statt 401 heißt genau das — gefragt werden darf.
        var client = _mitAnmeldung.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tournaments/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/chat/status")).StatusCode);
    }

    [Fact]
    public async Task Wer_ich_bin_verlangt_eine_Anmeldung()
    {
        // Mit Sitzung geht es tiefer, in SitzungTests — hier zählt, dass der
        // Weg hängt und nicht offen steht.
        var antwort = await _mitAnmeldung.CreateClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
    }

    [Fact]
    public void Verlangen_ohne_Client_Id_bricht_beim_Start_ab()
    {
        // Lieber hier laut als im Betrieb leise: Ohne Client-Id kann kein
        // Token gelten, die Anwendung wiese also jeden ab.
        using var halb = Bauen(builder => builder.UseSetting("Auth:Required", "true"));

        var fehler = Assert.Throws<InvalidOperationException>(() => halb.CreateClient());

        Assert.Contains("Auth__GoogleClientId", fehler.Message);
    }

    private WebApplicationFactory<Program> Bauen(Action<IWebHostBuilder> einstellen) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_path};Pooling=False");
            einstellen(builder);
        });
}
