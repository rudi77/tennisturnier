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
        // UseSetting und nicht ConfigureAppConfiguration: im minimalen Hostmodell
        // steht appsettings.json sonst später in der Kette und gewinnt — dann
        // liefen alle Tests gemeinsam auf matchday.db im Ausgabeverzeichnis.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_path}"));
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
            Assert.Equal(string.Empty, status.GetProperty("missing").GetString());
            return;
        }

        // Nicht nur dass etwas fehlt, sondern was: der Status trägt denselben
        // Satz, den das Gespräch als Fehler schicken würde. Fehlte er hier,
        // bliebe der Oberfläche nur ein fest verdrahteter Verdacht.
        Assert.Contains("AZURE_OPENAI_ENDPOINT", status.GetProperty("missing").GetString());

        var response = await rudi.PostAsJsonAsync("/api/chat", new { message = "Hallo" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: session", body);
        Assert.Contains("AZURE_OPENAI_ENDPOINT", body);
    }

    [Fact]
    public async Task Die_eigene_Liste_und_das_Loeschen()
    {
        var rudi = Client("browser-rudi");
        await rudi.PostAsJsonAsync("/api/tournaments", new { name = "Sommercup" });
        var zweites = await rudi.PostAsJsonAsync("/api/tournaments", new { name = "Herbstcup" });
        var id = (await zweites.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tournament").GetProperty("id").GetString();

        var meine = await rudi.GetFromJsonAsync<JsonElement>("/api/tournaments");
        // Das Neueste zuerst.
        Assert.Equal(["Herbstcup", "Sommercup"], meine.EnumerateArray().Select(t => t.GetProperty("name").GetString()));

        // Ein Fremder darf nicht löschen, der Eigentümer schon.
        var fremd = Client("browser-fremd");
        Assert.Equal(HttpStatusCode.Forbidden, (await fremd.DeleteAsync($"/api/tournaments/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await rudi.DeleteAsync($"/api/tournaments/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rudi.GetAsync($"/api/tournaments/{id}")).StatusCode);
        Assert.Single((await rudi.GetFromJsonAsync<JsonElement>("/api/tournaments")).EnumerateArray());
    }

    [Fact]
    public async Task Hinter_einem_Proxy_zaehlen_die_weitergegebenen_Kopfzeilen()
    {
        var rudi = Client("browser-rudi");
        rudi.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        rudi.DefaultRequestHeaders.Add("X-Forwarded-Host", "matchday.example");

        var created = await rudi.PostAsJsonAsync("/api/tournaments", new { name = "Sommercup" });
        var admin = await created.Content.ReadFromJsonAsync<JsonElement>();

        Assert.StartsWith("https://matchday.example/", admin.GetProperty("links").GetProperty("publicUrl").GetString());
        Assert.StartsWith("https://matchday.example/", admin.GetProperty("links").GetProperty("adminUrl").GetString());
    }

    [Fact]
    public async Task Eine_leere_oder_zu_lange_Nachricht_weist_der_Chat_zurueck()
    {
        var rudi = Client("browser-rudi");

        var leer = await rudi.PostAsJsonAsync("/api/chat", new { message = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, leer.StatusCode);
        Assert.Contains("Die Nachricht fehlt", (await leer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var zuLang = await rudi.PostAsJsonAsync("/api/chat", new { message = new string('a', 4001) });
        Assert.Equal(HttpStatusCode.BadRequest, zuLang.StatusCode);
    }

    [Fact]
    public async Task Ein_gespeichertes_Gespraech_kommt_ohne_Kontextblock_zurueck()
    {
        const string verlauf = """
            {
              "id": "sitzung-api",
              "clientId": "browser-rudi",
              "tournamentId": null,
              "messages": [
                { "role": "user", "blocks": [ { "kind": "Text", "text": "<context>Heute: 2026-09-12</context> Leg Sommercup an" } ] },
                {
                  "role": "assistant",
                  "blocks": [
                    { "kind": "Thinking", "text": "Erst anlegen." },
                    { "kind": "Text", "text": "Steht." },
                    { "kind": "ToolUse", "id": "ruf-1", "name": "create_tournament", "input": { "name": "Sommercup" } }
                  ]
                },
                { "role": "tool", "blocks": [ { "kind": "ToolResult", "toolUseId": "ruf-1", "text": "angelegt", "widget": "tournament" } ] }
              ]
            }
            """;
        await Verlauf("sitzung-api", verlauf);

        var gespraech = await Client("browser-rudi").GetFromJsonAsync<JsonElement>("/api/chat/sitzung-api");
        var nachrichten = gespraech.GetProperty("messages").EnumerateArray().ToList();

        // Der Gedanke bleibt drin, der Kontextblock nicht — und die Zeile mit dem
        // Werkzeugergebnis kommt wegen ihres Widgets mit, obwohl sie keinen Text hat.
        Assert.Equal(3, nachrichten.Count);
        Assert.Equal("Leg Sommercup an", nachrichten[0].GetProperty("text").GetString());
        Assert.Equal("Steht.", nachrichten[1].GetProperty("text").GetString());
        Assert.Equal(string.Empty, nachrichten[2].GetProperty("text").GetString());
        Assert.Equal(["tournament"], nachrichten[2].GetProperty("widgets").EnumerateArray().Select(w => w.GetString()));
    }

    [Fact]
    public async Task Eine_leere_Zeile_im_Verlauf_faellt_weg()
    {
        const string verlauf = """
            {
              "id": "sitzung-leer",
              "clientId": "browser-rudi",
              "tournamentId": null,
              "messages": [
                { "role": "assistant", "blocks": [ { "kind": "Thinking", "text": "Nur gedacht." } ] },
                { "role": "assistant", "blocks": [ { "kind": "Text", "text": null } ] }
              ]
            }
            """;
        await Verlauf("sitzung-leer", verlauf);

        var gespraech = await Client("browser-rudi").GetFromJsonAsync<JsonElement>("/api/chat/sitzung-leer");

        Assert.Empty(gespraech.GetProperty("messages").EnumerateArray());
    }

    [Fact]
    public async Task Ein_unlesbarer_Verlauf_wird_ein_Fehler_und_kein_Absturz()
    {
        await Verlauf("sitzung-kaputt", "{ das ist kein json");

        var antwort = await Client("browser-rudi").GetAsync("/api/chat/sitzung-kaputt");

        Assert.Equal(HttpStatusCode.InternalServerError, antwort.StatusCode);
        Assert.Equal("Da ist etwas schiefgegangen.", (await antwort.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    /// <summary>Schreibt einen Verlauf direkt in dieselbe Datenbank, die die Anwendung benutzt.</summary>
    private async Task Verlauf(string sitzung, string json)
    {
        var store = new Matchday.Server.Storage.TournamentStore($"Data Source={_path}");
        await store.SaveSessionJsonAsync(sitzung, "browser-rudi", json, CancellationToken.None);
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
