using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matchday.Domain;
using Matchday.Server.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Matchday.Server.Tests;

/// <summary>
/// Ein geteilter Mitschau-Link zeigt in der Gruppe, um welches Turnier es
/// geht — geschrieben vom Server, denn wer die Vorschau abholt, führt kein
/// Skript aus.
/// </summary>
public sealed class VorschauTests : IDisposable
{
    private const string Seite = """
        <!doctype html>
        <html lang="de">
          <head>
            <title>MATCHDAY</title>
            <meta name="description" content="Allgemein" />
            <meta property="og:title" content="MATCHDAY" />
            <meta property="og:description" content="Allgemein" />
            <meta property="og:image" content="/icon-512.png" />
          </head>
          <body><div id="root"></div></body>
        </html>
        """;

    private static readonly DateTimeOffset Jetzt = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"matchday-vorschau-{Guid.NewGuid():N}.db");
    private readonly string _web = Directory.CreateTempSubdirectory("matchday-web-").FullName;
    private readonly string _leer = Directory.CreateTempSubdirectory("matchday-leer-").FullName;
    private readonly List<WebApplicationFactory<Program>> _fabriken = [];

    public VorschauTests() => File.WriteAllText(Path.Combine(_web, "index.html"), Seite);

    public void Dispose()
    {
        foreach (var fabrik in _fabriken)
        {
            fabrik.Dispose();
        }

        Aufbau.Wegräumen(_path);
        Directory.Delete(_web, recursive: true);
        Directory.Delete(_leer, recursive: true);
    }

    [Fact]
    public async Task Der_Mitschau_Link_traegt_Name_und_Termin_in_die_Vorschau()
    {
        var client = Client(_web);
        var angelegt = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = "Abendrunde <am See> & \"Rudis\" Cup's",
            date = "2026-09-26",
            startTime = "18:30:00",
            location = "Baden",
            participants = new[] { "Anna", "Tom" },
        });
        var id = (await angelegt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tournament").GetProperty("id").GetString();

        var html = await client.GetStringAsync($"/?t={id}");

        Assert.Contains("<meta property=\"og:title\" content=\"Abendrunde &lt;am See&gt; &amp; &quot;Rudis&quot; Cup&#39;s\" />", html);
        Assert.Contains("<title>Abendrunde &lt;am See&gt; &amp; &quot;Rudis&quot; Cup&#39;s · MATCHDAY</title>", html);
        Assert.Contains("Sa., 26. September 2026 · 18:30 Uhr · Baden — Einzel, K.o., 2 Teilnehmer, noch nicht gestartet", html);
        Assert.Contains("<meta property=\"og:image\" content=\"http://localhost/icon-512.png\" />", html);
        Assert.Contains($"<meta property=\"og:url\" content=\"http://localhost/?t={id}\" />", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/?t=kein-turnier")]
    [InlineData("/?t=8f6b1f26-58b3-4b9e-9f6e-2f0a2f7a1c11")]
    public async Task Ohne_Turnier_bleibt_die_allgemeine_Vorschau_mit_absolutem_Bild(string adresse)
    {
        var html = await Client(_web).GetStringAsync(adresse);

        Assert.Contains("<meta property=\"og:title\" content=\"MATCHDAY\" />", html);
        Assert.Contains("<title>MATCHDAY</title>", html);
        Assert.Contains("content=\"http://localhost/icon-512.png\"", html);
    }

    [Fact]
    public async Task Ohne_gebaute_Oberflaeche_und_fuer_alles_andere_greift_sie_nicht()
    {
        // Ohne Seite schreibt die Vorschau nichts um — was danach kommt, ist
        // Sache der gewöhnlichen Auslieferung.
        Assert.DoesNotContain("http://localhost/icon-512.png", await (await Client(_leer).GetAsync("/")).Content.ReadAsStringAsync());

        var client = Client(_web);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("/", null)).StatusCode);
    }

    [Fact]
    public void Die_Beschreibung_sagt_was_da_ist_und_wie_es_steht()
    {
        var nackt = Tournament.Create("Cup", "b", Jetzt, mode: Mode.RoundRobin, discipline: Discipline.Doubles);
        Assert.Equal("Doppel, jeder gegen jeden, 0 Teams, noch nicht gestartet", Preview.Beschreibe(nackt));

        var t = Tournament.Create("Cup", "b", Jetzt, date: new DateOnly(2026, 9, 26));
        t.AddParticipant("Anna");
        t.AddParticipant("Tom");
        t.Draw(new Random(1));
        t.Start(Jetzt);
        Assert.Equal("Sa., 26. September 2026 — Einzel, K.o., 2 Teilnehmer, läuft gerade — live mitschauen", Preview.Beschreibe(t));

        t.RecordResult(t.Matches[0].Id, Score.Walkover(absentSide: 2));
        Assert.EndsWith("abgeschlossen", Preview.Beschreibe(t));
    }

    [Fact]
    public void Fehlt_ein_Eintrag_wird_er_angehaengt()
    {
        var html = Preview.Render("<html><head></head></html>", null, "https://m.example", "/");

        Assert.Contains("<meta property=\"og:image\" content=\"https://m.example/icon-512.png\" />", html);
        Assert.Contains("<meta property=\"og:url\" content=\"https://m.example/\" />", html);
    }

    private HttpClient Client(string webroot)
    {
        var fabrik = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_path};Pooling=False");
            builder.ConfigureTestServices(services => services.AddSingleton(new IndexPage(new PhysicalFileProvider(webroot))));
        });
        _fabriken.Add(fabrik);

        var client = fabrik.CreateClient();
        client.DefaultRequestHeaders.Add(Endpoints.ClientHeader, "browser-rudi");
        return client;
    }
}
