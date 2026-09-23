using System.Text.Json;
using Matchday.Server.Agent;
using Matchday.Server.Api;
using Microsoft.Extensions.AI;

namespace Matchday.Server.Tests;

/// <summary>
/// Ein Gespräch je Turnier: Ein allgemeines findet zu dem Turnier, das darin
/// entsteht, ein gebundenes bleibt bei seinem — und mit dem Turnier gehen auch
/// die Gespräche darüber.
/// </summary>
public sealed class GespraechJeTurnierTests : IDisposable
{
    private readonly Aufbau _a = new();

    public void Dispose() => _a.Dispose();

    private static FunctionCallContent Aufruf(string name, object argumente, string id = "ruf-1") =>
        new(id, name, JsonSerializer.SerializeToElement(argumente).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value));

    private static async Task<List<ChatEvent>> Lauf(TournamentAgent agent, ChatRequest anfrage)
    {
        var ereignisse = new List<ChatEvent>();

        await foreach (var e in agent.RunAsync(anfrage, new Actor("browser-rudi", null), "https://matchday.test", CancellationToken.None))
        {
            ereignisse.Add(e);
        }

        return ereignisse;
    }

    private static JsonElement Ende(List<ChatEvent> ereignisse) =>
        JsonSerializer.SerializeToElement(ereignisse.Single(e => e.Type == "done").Data);

    private static string? Id(JsonElement e, string name) =>
        e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : e.GetProperty(name).GetString();

    [Fact]
    public async Task Ein_allgemeines_Gespraech_gehoert_dem_Turnier_das_darin_entsteht()
    {
        var modell = new Modell([Aufruf("create_tournament", new { name = "Sommercup" })], [new TextContent("Angelegt.")]);
        var ende = Ende(await Lauf(_a.Agent(modell), new ChatRequest("Leg den Sommercup an", "allgemein")));

        var turnier = Guid.Parse(Id(ende, "tournamentId")!);
        Assert.Equal(turnier.ToString(), Id(ende, "activeTournamentId"));

        var gefunden = await _a.Agent(modell).SessionForTournamentAsync(turnier, _a.Rudi, CancellationToken.None);
        Assert.Equal("allgemein", gefunden!.Id);

        // Ein anderer Browser sieht das Gespräch nicht.
        Assert.Null(await _a.Agent(modell).SessionForTournamentAsync(turnier, _a.Fremder, CancellationToken.None));
    }

    [Fact]
    public async Task Ein_gebundenes_Gespraech_bleibt_bei_seinem_Turnier()
    {
        var erstes = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Erstes"));

        // Die Oberfläche nennt das Turnier; ein zweites Turnier im selben
        // Gespräch nimmt es ihm nicht weg.
        var modell = new Modell([Aufruf("create_tournament", new { name = "Zweites" })], [new TextContent("Angelegt.")]);
        var ende = Ende(await Lauf(_a.Agent(modell), new ChatRequest("Leg noch eins an", "zu-erstem", erstes.Id)));

        Assert.Equal(erstes.Id.ToString(), Id(ende, "tournamentId"));
        Assert.NotEqual(erstes.Id.ToString(), Id(ende, "activeTournamentId"));

        // Auch eine später genannte Id ändert nichts mehr an der Bindung.
        var anderes = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Drittes"));
        var weiter = Ende(await Lauf(_a.Agent(new Modell([new TextContent("Ja.")])), new ChatRequest("Und?", "zu-erstem", anderes.Id)));
        Assert.Equal(erstes.Id.ToString(), Id(weiter, "tournamentId"));
    }

    [Fact]
    public async Task Ist_das_Turnier_geloescht_wird_das_Gespraech_wieder_allgemein()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Weg damit"));

        var modell = new Modell([Aufruf("delete_tournament", new { tournamentId = t.Id.ToString() })], [new TextContent("Gelöscht.")]);
        var ende = Ende(await Lauf(_a.Agent(modell), new ChatRequest("Lösch es", "zum-loeschen", t.Id)));

        Assert.Null(Id(ende, "tournamentId"));
        Assert.Null(Id(ende, "activeTournamentId"));

        // Das Gespräch selbst gibt es noch — nur keinem Turnier mehr zugeordnet.
        var session = await _a.Agent(modell).LoadSessionAsync("zum-loeschen", _a.Rudi, CancellationToken.None);
        Assert.Null(session.TournamentId);
        Assert.NotEmpty(session.Messages);
    }

    [Fact]
    public async Task Mit_dem_Turnier_gehen_die_Gespraeche_darueber()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Cup"));
        await _a.Store.SaveSessionJsonAsync("rudi-cup", "browser-rudi", "{\"id\":\"rudi-cup\",\"clientId\":\"browser-rudi\"}", t.Id);
        await _a.Store.SaveSessionJsonAsync("max-cup", "browser-max", "{\"id\":\"max-cup\",\"clientId\":\"browser-max\"}", t.Id);
        await _a.Store.SaveSessionJsonAsync("allgemein", "browser-rudi", "{\"id\":\"allgemein\",\"clientId\":\"browser-rudi\"}");

        await _a.Actions.DeleteAsync(_a.Rudi, t.Id);

        Assert.Null(await _a.Store.FindSessionJsonAsync("rudi-cup", "browser-rudi"));
        Assert.Null(await _a.Store.FindSessionJsonAsync("max-cup", "browser-max"));
        Assert.NotNull(await _a.Store.FindSessionJsonAsync("allgemein", "browser-rudi"));
    }

    [Fact]
    public async Task Ein_Gespraech_loescht_nur_wer_es_gefuehrt_hat()
    {
        await _a.Store.SaveSessionJsonAsync("meins", "browser-rudi", "{\"id\":\"meins\",\"clientId\":\"browser-rudi\"}");
        var agent = _a.Agent(new Modell());

        Assert.False(await agent.DeleteSessionAsync("meins", _a.Fremder, CancellationToken.None));
        Assert.True(await agent.DeleteSessionAsync("meins", _a.Rudi, CancellationToken.None));
        Assert.False(await agent.DeleteSessionAsync("meins", _a.Rudi, CancellationToken.None));
    }

    [Fact]
    public async Task Das_neueste_Gespraech_eines_Turniers_gilt()
    {
        var t = Guid.NewGuid();
        await _a.Store.SaveSessionJsonAsync("alt", "browser-rudi", "{\"id\":\"alt\",\"clientId\":\"browser-rudi\"}", t);
        await Task.Delay(20);
        await _a.Store.SaveSessionJsonAsync("neu", "browser-rudi", "{\"id\":\"neu\",\"clientId\":\"browser-rudi\"}", t);

        var json = await _a.Store.FindSessionJsonForTournamentAsync(t, "browser-rudi");
        Assert.Contains("\"neu\"", json);
    }
}
