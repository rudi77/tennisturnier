using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matchday.Server.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Matchday.Server.Tests;

/// <summary>Die HTTP-API von außen: Kopfzeilen, Statuscodes, Fehlerform.</summary>
public sealed class ApiTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"matchday-api-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = $"Data Source={_path}",
                })));
    }

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private HttpClient Client(string browser, string? token = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(Endpoints.ClientHeader, browser);

        if (token is not null)
        {
            client.DefaultRequestHeaders.Add(Endpoints.AdminHeader, token);
        }

        return client;
    }

    [Fact]
    public async Task Anlegen_ansehen_und_die_Kopfzeilen_entscheiden()
    {
        var rudi = Client("browser-rudi");

        var created = await rudi.PostAsJsonAsync("/api/tournaments", new { name = "Sommercup", mode = "Knockout" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var admin = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = admin.GetProperty("tournament").GetProperty("id").GetString();
        var token = admin.GetProperty("adminToken").GetString();
        Assert.Contains($"?t={id}", admin.GetProperty("links").GetProperty("publicUrl").GetString());

        // Öffentlich, ohne Kopfzeile: die Sicht ohne Token.
        var anonym = _factory.CreateClient();
        var view = await anonym.GetFromJsonAsync<JsonElement>($"/api/tournaments/{id}");
        Assert.Equal("Sommercup", view.GetProperty("name").GetString());
        Assert.False(view.TryGetProperty("adminToken", out _));

        // Ein Fremder darf nicht ändern — mit Token schon.
        var fremd = Client("browser-fremd");
        var verboten = await fremd.PostAsJsonAsync($"/api/tournaments/{id}/participants", new { names = new[] { "Max" } });
        Assert.Equal(HttpStatusCode.Forbidden, verboten.StatusCode);

        var mitToken = Client("browser-fremd", token);
        var erlaubt = await mitToken.PostAsJsonAsync($"/api/tournaments/{id}/participants", new { names = new[] { "Max", "Max" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, erlaubt.StatusCode);
        var fehler = await erlaubt.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("schon auf der Liste", fehler.GetProperty("error").GetString());

        var ohneKopfzeile = await anonym.GetAsync("/api/tournaments");
        Assert.Equal(HttpStatusCode.Forbidden, ohneKopfzeile.StatusCode);

        var nichtDa = await anonym.GetAsync($"/api/tournaments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, nichtDa.StatusCode);

        var byAdmin = await fremd.GetFromJsonAsync<JsonElement>($"/api/tournaments/by-admin/{token}");
        Assert.Equal(id, byAdmin.GetProperty("tournament").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Der_Chat_sagt_ohne_Schluessel_was_fehlt()
    {
        var rudi = Client("browser-rudi");
        var status = await rudi.GetFromJsonAsync<JsonElement>("/api/chat/status");

        // In der Testumgebung ist kein Schlüssel gesetzt; falls doch, prüfen wir nur die Form.
        if (status.GetProperty("configured").GetBoolean())
        {
            return;
        }

        var response = await rudi.PostAsJsonAsync("/api/chat", new { message = "Hallo" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: session", body);
        Assert.Contains("ANTHROPIC_API_KEY", body);
    }

    [Fact]
    public async Task Die_Live_Ansicht_schickt_zuerst_die_Sicht()
    {
        var rudi = Client("browser-rudi");
        var created = await rudi.PostAsJsonAsync("/api/tournaments", new { name = "Sommercup" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tournament").GetProperty("id").GetString();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await _factory.CreateClient().GetAsync($"/api/tournaments/{id}/live", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        Assert.Equal("event: view", await reader.ReadLineAsync(cts.Token));
        var data = await reader.ReadLineAsync(cts.Token);
        Assert.StartsWith("data: {", data);
        Assert.Contains("Sommercup", data);
    }
}
