using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matchday.Server.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Matchday.Server.Tests;

/// <summary>
/// Live zählen und der Eintragen-Link, von außen: wer was darf, und dass der
/// Eintragen-Link nie den Verwalterlink preisgibt.
/// </summary>
public sealed class EintragenApiTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"matchday-eintragen-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public EintragenApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_path};Pooling=False"));
    }

    public void Dispose()
    {
        _factory.Dispose();
        Aufbau.Wegräumen(_path);
    }

    private HttpClient Client(string browser, string? admin = null, string? scorer = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(Endpoints.ClientHeader, browser);

        if (admin is not null)
        {
            client.DefaultRequestHeaders.Add(Endpoints.AdminHeader, admin);
        }

        if (scorer is not null)
        {
            client.DefaultRequestHeaders.Add(Endpoints.ScorerHeader, scorer);
        }

        return client;
    }

    /// <summary>Ein ausgelostes Turnier mit zwei Spielern, ein Satz: ein Match, sofort spielbar.</summary>
    private async Task<(string Id, string Match, string ScorerUrl)> Ausgelost()
    {
        var rudi = Client("browser-rudi");
        var created = await rudi.PostAsJsonAsync("/api/tournaments", new
        {
            name = "Live-Cup",
            format = new { bestOf = 1, finalSetMode = "Regular", tiebreakAt = 6 },
            participants = new[] { "Anna", "Tom" },
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tournament").GetProperty("id").GetString()!;
        (await rudi.PostAsync($"/api/tournaments/{id}/draw", null)).EnsureSuccessStatusCode();
        var drawn = await (await rudi.PostAsync($"/api/tournaments/{id}/start", null)).Content.ReadFromJsonAsync<JsonElement>();

        var match = drawn.GetProperty("tournament").GetProperty("matches")[0].GetProperty("id").GetString()!;
        var scorerUrl = drawn.GetProperty("links").GetProperty("scorerUrl").GetString()!;
        return (id, match, scorerUrl);
    }

    [Fact]
    public async Task Ein_ueberholter_Schritt_kommt_als_409_zurueck()
    {
        var (id, match, _) = await Ausgelost();
        var rudi = Client("browser-rudi");

        (await rudi.PostAsJsonAsync($"/api/tournaments/{id}/matches/{match}/live", new { action = "Point", side = 1, after = 0 })).EnsureSuccessStatusCode();
        var zweimal = await rudi.PostAsJsonAsync($"/api/tournaments/{id}/matches/{match}/live", new { action = "Point", side = 1, after = 0 });

        Assert.Equal(HttpStatusCode.Conflict, zweimal.StatusCode);
        Assert.Contains("geändert", (await zweimal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Mit_dem_Eintragen_Link_zaehlt_jeder_mit_aber_verwaltet_nicht()
    {
        var (id, match, scorerUrl) = await Ausgelost();
        Assert.Contains("?s=", scorerUrl);
        var token = scorerUrl[(scorerUrl.IndexOf("?s=", StringComparison.Ordinal) + 3)..];

        // Den Link einlösen: die Sicht und das eigene Token, nie das der Verwaltung.
        var helfer = Client("browser-helfer", scorer: token);
        var zugang = await helfer.GetFromJsonAsync<JsonElement>($"/api/tournaments/by-scorer/{token}");
        Assert.Equal(id, zugang.GetProperty("tournament").GetProperty("id").GetString());
        Assert.Equal(token, zugang.GetProperty("scorerToken").GetString());
        Assert.False(zugang.TryGetProperty("adminToken", out _));

        var punkt = await helfer.PostAsJsonAsync($"/api/tournaments/{id}/matches/{match}/live", new { action = "Point", side = 1 });
        Assert.Equal(HttpStatusCode.OK, punkt.StatusCode);
        var nachPunkt = await punkt.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(nachPunkt.TryGetProperty("adminToken", out _));
        var live = nachPunkt.GetProperty("tournament").GetProperty("matches")[0].GetProperty("live");
        Assert.Equal("15", live.GetProperty("points1").GetString());
        Assert.Equal("Playing", nachPunkt.GetProperty("tournament").GetProperty("matches")[0].GetProperty("status").GetString());

        var spiel = await helfer.PostAsJsonAsync($"/api/tournaments/{id}/matches/{match}/live", new { action = "Game", side = 2 });
        var nachSpiel = await spiel.Content.ReadFromJsonAsync<JsonElement>();
        live = nachSpiel.GetProperty("tournament").GetProperty("matches")[0].GetProperty("live");
        Assert.Equal(1, live.GetProperty("games2").GetInt32());
        Assert.Equal("0:1", string.Join(":", live.GetProperty("sets")[0].GetProperty("games1").GetInt32(), live.GetProperty("sets")[0].GetProperty("games2").GetInt32()));

        var zurueck = await helfer.PostAsJsonAsync($"/api/tournaments/{id}/matches/{match}/live", new { action = "Undo" });
        Assert.Equal(1, (await zurueck.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tournament").GetProperty("matches")[0].GetProperty("live").GetProperty("events").GetInt32());

        // Ergebnis eintragen und zurücknehmen darf der Helfer auch.
        var ergebnis = await helfer.PutAsJsonAsync($"/api/tournaments/{id}/matches/{match}/result", new { kind = "Played", winnerSide = 1, sets = new[] { new { games1 = 6, games2 = 2 } } });
        Assert.Equal(HttpStatusCode.OK, ergebnis.StatusCode);
        Assert.False((await ergebnis.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("adminToken", out _));
        Assert.Equal(HttpStatusCode.OK, (await helfer.DeleteAsync($"/api/tournaments/{id}/matches/{match}/result")).StatusCode);

        // Alles andere nicht.
        Assert.Equal(HttpStatusCode.Forbidden, (await helfer.DeleteAsync($"/api/tournaments/{id}/draw")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await helfer.PostAsJsonAsync($"/api/tournaments/{id}/participants", new { names = new[] { "Max" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await helfer.DeleteAsync($"/api/tournaments/{id}")).StatusCode);
    }

    [Fact]
    public async Task Ohne_Link_darf_niemand_eintragen()
    {
        var (id, match, _) = await Ausgelost();

        var fremd = Client("browser-fremd");
        Assert.Equal(HttpStatusCode.Forbidden, (await fremd.PostAsJsonAsync($"/api/tournaments/{id}/matches/{match}/live", new { action = "Point", side = 1 })).StatusCode);

        var falsch = Client("browser-fremd", scorer: "geraten");
        Assert.Equal(HttpStatusCode.Forbidden, (await falsch.PostAsJsonAsync($"/api/tournaments/{id}/matches/{match}/live", new { action = "Point", side = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await falsch.GetAsync("/api/tournaments/by-scorer/geraten")).StatusCode);
    }

    [Fact]
    public async Task Die_Verwaltung_zaehlt_mit_und_bekommt_ihre_Sicht()
    {
        var (id, match, _) = await Ausgelost();
        var rudi = Client("browser-rudi");

        var punkt = await rudi.PostAsJsonAsync($"/api/tournaments/{id}/matches/{match}/live", new { action = "Point", side = 2 });
        var sicht = await punkt.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(sicht.TryGetProperty("adminToken", out _));
        Assert.True(sicht.TryGetProperty("links", out _));

        var falscheSeite = await rudi.PostAsJsonAsync($"/api/tournaments/{id}/matches/{match}/live", new { action = "Point", side = 3 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, falscheSeite.StatusCode);

        // Ein Punkt ist gespielt: Der Rahmen steht jetzt fest.
        var aendern = await rudi.PostAsJsonAsync($"/api/tournaments/{id}/participants", new { names = new[] { "Max" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, aendern.StatusCode);
    }

    [Fact]
    public async Task Ein_Gespraech_je_Turnier_ueber_die_API()
    {
        var (id, _, _) = await Ausgelost();
        var rudi = Client("browser-rudi");

        // Noch nichts gesagt: ein leeres Gespräch ohne Id.
        var leer = await rudi.GetFromJsonAsync<JsonElement>($"/api/chat/tournament/{id}");
        Assert.Equal(JsonValueKind.Null, leer.GetProperty("id").ValueKind);
        Assert.Equal(0, leer.GetProperty("messages").GetArrayLength());

        var store = new Matchday.Server.Storage.TournamentStore($"Data Source={_path};Pooling=False");
        await store.SaveSessionJsonAsync("zum-cup", "browser-rudi", $$"""
            {"id":"zum-cup","clientId":"browser-rudi","tournamentId":"{{id}}","messages":[
              {"role":"user","blocks":[{"kind":"Text","text":"<context>x</context>\n\nWie steht es?"}]},
              {"role":"assistant","blocks":[{"kind":"Text","text":"Anna führt."}]}
            ]}
            """, Guid.Parse(id));

        var voll = await rudi.GetFromJsonAsync<JsonElement>($"/api/chat/tournament/{id}");
        Assert.Equal("zum-cup", voll.GetProperty("id").GetString());
        Assert.Equal("Wie steht es?", voll.GetProperty("messages")[0].GetProperty("text").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await Client("browser-fremd").DeleteAsync("/api/chat/zum-cup")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await rudi.DeleteAsync("/api/chat/zum-cup")).StatusCode);

        var wieder = await rudi.GetFromJsonAsync<JsonElement>($"/api/chat/tournament/{id}");
        Assert.Equal(JsonValueKind.Null, wieder.GetProperty("id").ValueKind);
    }
}
