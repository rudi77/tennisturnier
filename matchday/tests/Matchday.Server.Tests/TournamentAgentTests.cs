using System.Text.Json;
using Matchday.Domain;
using Matchday.Server.Agent;
using Matchday.Server.Api;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Matchday.Server.Tests;

/// <summary>
/// Das Gespräch mit einem Modell, das vorliest: welche Ereignisse die
/// Oberfläche sieht, und was von der Runde in der Sitzung stehen bleibt.
/// </summary>
public sealed class TournamentAgentTests : IDisposable
{
    private readonly Aufbau _a = new();

    public void Dispose() => _a.Dispose();

    [Fact]
    public async Task Ohne_Modellzugang_sagt_der_Agent_was_fehlt()
    {
        var ereignisse = await Lauf(OhneModell(), new ChatRequest("Hallo"));

        Assert.Equal(["session", "error"], ereignisse.Select(e => e.Type));
        Assert.Contains("AZURE_OPENAI_ENDPOINT", Daten(ereignisse[1]).GetProperty("message").GetString());
    }

    [Fact]
    public void Der_Grund_steht_auch_ohne_Gespraech_bereit()
    {
        // Die Oberfläche fragt den Status, bevor jemand etwas eintippt. Käme
        // der Grund erst als Fehlerereignis eines Gesprächs, stünde über dem
        // stummen Eingabefeld nichts als eine Vermutung.
        Assert.Contains("AZURE_OPENAI_ENDPOINT", OhneModell().Missing);
        Assert.Equal(string.Empty, _a.Agent(new Modell()).Missing);
    }

    [Fact]
    public async Task Ein_Satz_wird_ein_Textereignis()
    {
        var modell = new Modell([new TextContent("Sag mir, wie das Turnier heißen soll.")]);

        var ereignisse = await Lauf(_a.Agent(modell), new ChatRequest("Hallo"));

        Assert.Equal(["session", "text", "done"], ereignisse.Select(e => e.Type));
        Assert.Equal("Sag mir, wie das Turnier heißen soll.", Daten(ereignisse[1]).GetProperty("text").GetString());
    }

    [Fact]
    public async Task Stueckweiser_Text_wird_eine_Sprechblase()
    {
        var modell = new Modell([new TextContent("Alles "), new TextContent("klar"), new TextContent(".")]);

        var ereignisse = await Lauf(_a.Agent(modell), new ChatRequest("Hallo"));

        var texte = ereignisse.Where(e => e.Type == "text").ToList();
        Assert.Single(texte);
        Assert.Equal("Alles klar.", Daten(texte[0]).GetProperty("text").GetString());
    }

    [Fact]
    public async Task Ein_Werkzeug_gibt_Text_Aufruf_und_Widget()
    {
        var modell = new Modell(
            [new TextContent("Lege ich an."), Aufruf("create_tournament", new { name = "Sommercup" })],
            [new TextContent("Fertig — wer spielt mit?")]);

        var ereignisse = await Lauf(_a.Agent(modell), new ChatRequest("Leg ein Turnier Sommercup an"));

        Assert.Equal(["session", "text", "tool", "widget", "text", "done"], ereignisse.Select(e => e.Type));

        var werkzeug = Daten(ereignisse[2]);
        Assert.Equal("create_tournament", werkzeug.GetProperty("name").GetString());
        Assert.Equal("Sommercup", werkzeug.GetProperty("input").GetProperty("name").GetString());

        var widget = Daten(ereignisse[3]);
        Assert.Equal(AgentTools.WidgetTournament, widget.GetProperty("widget").GetString());
        var id = widget.GetProperty("tournamentId").GetString();
        Assert.NotNull(id);

        // Das angelegte Turnier ist das aktuelle und bleibt es.
        Assert.Equal(id, Daten(ereignisse[5]).GetProperty("tournamentId").GetString());
    }

    [Fact]
    public async Task Ein_zurueckgewiesenes_Werkzeug_zeigt_kein_Widget()
    {
        var modell = new Modell(
            [Aufruf("create_tournament", new { name = "   " })],
            [new TextContent("Wie soll es heißen?")]);

        var ereignisse = await Lauf(_a.Agent(modell), new ChatRequest("Leg was an"));

        Assert.Equal(["session", "tool", "text", "done"], ereignisse.Select(e => e.Type));
        Assert.Null(Daten(ereignisse[3]).GetProperty("tournamentId").ValueKind == JsonValueKind.Null ? null : "gesetzt");
    }

    [Fact]
    public async Task Gedanken_stehen_in_der_Sitzung_aber_nicht_in_der_Oberflaeche()
    {
        var modell = new Modell([new TextReasoningContent("Erst nachfragen.") { ProtectedData = "siegel" }, new TextContent("Wie heißt es?")]);
        var agent = _a.Agent(modell);

        var ereignisse = await Lauf(agent, new ChatRequest("Hallo", "sitzung-1"));

        Assert.Equal(["session", "text", "done"], ereignisse.Select(e => e.Type));

        var sitzung = await agent.LoadSessionAsync("sitzung-1", _a.Rudi, CancellationToken.None);
        var gedanke = sitzung.Messages.SelectMany(m => m.Blocks).Single(b => b.Kind == BlockKind.Thinking);
        Assert.Equal("Erst nachfragen.", gedanke.Text);
        Assert.Equal("siegel", gedanke.Signature);
    }

    [Fact]
    public async Task Die_naechste_Runde_liest_den_ganzen_Verlauf()
    {
        var erstes = new Modell(
            [new TextReasoningContent("Anlegen."), Aufruf("create_tournament", new { name = "Sommercup" })],
            [new TextContent("Steht.")]);
        await Lauf(_a.Agent(erstes), new ChatRequest("Leg Sommercup an", "sitzung-2"));

        var zweites = new Modell([new TextContent("Rudi und Max sind drin.")]);
        await Lauf(_a.Agent(zweites), new ChatRequest("Wer spielt mit?", "sitzung-2"));

        var gelesen = zweites.Anfragen.Single();

        // Frage, Gedanke samt Aufruf, Werkzeugergebnis, Antwort, neue Frage.
        Assert.Equal(
            [ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant, ChatRole.User],
            gelesen.Select(m => m.Role));

        Assert.Contains(gelesen[1].Contents, c => c is TextReasoningContent);
        var aufruf = Assert.IsType<FunctionCallContent>(gelesen[1].Contents.Last());
        Assert.Equal("create_tournament", aufruf.Name);
        Assert.Equal("Sommercup", aufruf.Arguments!["name"]!.ToString());
        Assert.IsType<FunctionResultContent>(gelesen[2].Contents.Single());
    }

    [Fact]
    public async Task Ein_Aufruf_ohne_Argumente_uebersteht_die_Sitzung()
    {
        var erstes = new Modell(
            [new FunctionCallContent("ruf-1", "list_tournaments")],
            [new TextContent("Noch keines.")]);
        await Lauf(_a.Agent(erstes), new ChatRequest("Was habe ich?", "sitzung-3"));

        var zweites = new Modell([new TextContent("Wie gesagt: keines.")]);
        await Lauf(_a.Agent(zweites), new ChatRequest("Und jetzt?", "sitzung-3"));

        var aufruf = zweites.Anfragen.Single().SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("list_tournaments", aufruf.Name);
        Assert.Empty(aufruf.Arguments ?? new Dictionary<string, object?>());
    }

    [Fact]
    public async Task Ein_Modellfehler_bleibt_ein_Satz_und_kein_Absturz()
    {
        var modell = new Modell { Fehler = new HttpRequestException("kein Netz") };
        var agent = _a.Agent(modell);

        var ereignisse = await Lauf(agent, new ChatRequest("Hallo", "sitzung-4"));

        Assert.Equal(["session", "error"], ereignisse.Select(e => e.Type));
        Assert.Contains("nicht erreichbar", Daten(ereignisse[1]).GetProperty("message").GetString());

        // Die Frage bleibt stehen, Bruchstücke der Antwort nicht.
        var sitzung = await agent.LoadSessionAsync("sitzung-4", _a.Rudi, CancellationToken.None);
        Assert.Equal(["user"], sitzung.Messages.Select(m => m.Role));
    }

    [Fact]
    public async Task Ein_Fehler_nach_einem_Werkzeug_haelt_das_Turnier_fest()
    {
        var modell = new Modell([Aufruf("create_tournament", new { name = "Sommercup" })])
        {
            Fehler = new HttpRequestException("kein Netz"),
            FehlerInRunde = 2,
        };
        var agent = _a.Agent(modell);

        var ereignisse = await Lauf(agent, new ChatRequest("Leg Sommercup an", "sitzung-5"));

        Assert.Equal(["session", "tool", "widget", "error"], ereignisse.Select(e => e.Type));

        var sitzung = await agent.LoadSessionAsync("sitzung-5", _a.Rudi, CancellationToken.None);
        Assert.NotNull(sitzung.TournamentId);
    }

    [Fact]
    public async Task Der_Agent_ruft_nicht_endlos_Werkzeuge()
    {
        // Ein Modell, das immer nur wieder nachsieht.
        var runden = Enumerable.Range(0, 20)
            .Select(i => (IReadOnlyList<AIContent>)[Aufruf("list_tournaments", new { }, $"ruf-{i}")])
            .ToArray();
        var modell = new Modell(runden);

        var ereignisse = await Lauf(_a.Agent(modell, new AgentOptions { MaxToolRounds = 2 }), new ChatRequest("Sieh nach"));

        Assert.Equal(2, ereignisse.Count(e => e.Type == "tool"));
        Assert.Equal("done", ereignisse[^1].Type);
    }

    [Fact]
    public async Task Anweisungen_Grenzen_und_Werkzeuge_gehen_mit()
    {
        var modell = new Modell([new TextContent("Ja.")]);
        var einstellungen = new AgentOptions { MaxTokens = 512, Effort = "high" };

        await Lauf(_a.Agent(modell, einstellungen), new ChatRequest("Hallo"));

        Assert.Equal(TournamentAgent.SystemPrompt, modell.Optionen!.Instructions);
        Assert.Equal(512, modell.Optionen.MaxOutputTokens);
        Assert.Equal(ReasoningEffort.High, modell.Optionen.Reasoning!.Effort);
        Assert.Equal(AgentTools.Definitions.Count, modell.Optionen.Tools!.Count);
    }

    [Fact]
    public async Task Das_Turnier_aus_der_Anfrage_wird_das_aktuelle()
    {
        var angelegt = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"), CancellationToken.None);
        var modell = new Modell([new TextContent("Das kenne ich.")]);

        await Lauf(_a.Agent(modell), new ChatRequest("Was steht an?", "sitzung-6", angelegt.Id));

        var gelesen = modell.Anfragen.Single().Single().Text;
        Assert.Contains($"Aktuelles Turnier: „Sommercup“ (id {angelegt.Id}, Einzel, K.o.,", gelesen);
    }

    [Fact]
    public async Task Ein_erfundenes_Werkzeug_hinterlaesst_keine_Spur()
    {
        var modell = new Modell(
            [Aufruf("fliegen", new { hoehe = 3 })],
            [new TextContent("Das kann ich nicht.")]);
        var agent = _a.Agent(modell);

        var ereignisse = await Lauf(agent, new ChatRequest("Flieg mal", "sitzung-7"));

        // Nichts ist gelaufen, also gibt es nichts zu zeigen.
        Assert.Equal(["session", "text", "done"], ereignisse.Select(e => e.Type));

        // Und im Verlauf steht weder die Frage nach dem Fliegen noch eine Antwort
        // darauf: beides würde die nächste Runde beim Modell abgewiesen werden.
        var sitzung = await agent.LoadSessionAsync("sitzung-7", _a.Rudi, CancellationToken.None);
        var arten = sitzung.Messages.SelectMany(m => m.Blocks).Select(b => b.Kind).ToList();
        Assert.DoesNotContain(BlockKind.ToolUse, arten);
        Assert.DoesNotContain(BlockKind.ToolResult, arten);
    }

    [Fact]
    public async Task Ein_halber_Verlauf_aus_der_Datenbank_wirft_niemanden_um()
    {
        // So etwas steht dort nach einem Stand von früher: Blöcke ohne Text,
        // ohne Namen, ohne Id. Gelesen werden muss es trotzdem.
        const string halb = """
            {
              "id": "sitzung-8",
              "clientId": "browser-rudi",
              "tournamentId": null,
              "messages": [
                {
                  "role": "assistant",
                  "blocks": [
                    { "kind": "Text", "text": null },
                    { "kind": "Thinking", "text": null, "signature": null },
                    { "kind": "ToolUse", "id": null, "name": null, "input": null }
                  ]
                },
                { "role": "tool", "blocks": [ { "kind": "ToolResult", "toolUseId": null, "text": null } ] }
              ]
            }
            """;
        await _a.Store.SaveSessionJsonAsync("sitzung-8", _a.Rudi.ClientId, halb, CancellationToken.None);

        var modell = new Modell([new TextContent("Fangen wir neu an.")]);
        var ereignisse = await Lauf(_a.Agent(modell), new ChatRequest("Wo waren wir?", "sitzung-8"));

        Assert.Equal(["session", "text", "done"], ereignisse.Select(e => e.Type));

        var gelesen = modell.Anfragen.Single();
        Assert.Equal([ChatRole.Assistant, ChatRole.Tool, ChatRole.User], gelesen.Select(m => m.Role));
        Assert.Equal(string.Empty, Assert.IsType<TextContent>(gelesen[0].Contents[0]).Text);
        Assert.Equal(string.Empty, Assert.IsType<FunctionCallContent>(gelesen[0].Contents[2]).Name);
        Assert.Equal(string.Empty, Assert.IsType<FunctionResultContent>(gelesen[1].Contents[0]).CallId);
    }

    [Fact]
    public async Task Ein_fremdes_Turnier_wird_trotzdem_nachgesehen()
    {
        // Wer den Verwalterlink hat, redet über ein Turnier, das nicht in seiner
        // eigenen Liste steht. Der Kontext muss es trotzdem benennen.
        var fremd = await _a.Actions.CreateAsync(_a.Fremder, new CreateTournamentRequest("Herbstcup"), CancellationToken.None);
        var modell = new Modell([new TextContent("Ja.")]);

        await Lauf(_a.Agent(modell), new ChatRequest("Wie steht es?", "sitzung-9", fremd.Id));

        Assert.Contains($"Aktuelles Turnier: „Herbstcup“ (id {fremd.Id}, Einzel, K.o.,", modell.Anfragen.Single().Single().Text);
    }

    [Fact]
    public async Task Der_Kontext_sagt_was_fuer_ein_Turnier_es_ist()
    {
        // Ohne die Disziplin im Kontext trüge der Agent im Doppel zwei Spieler
        // als zwei Teams ein — und bekäme eine Absage für etwas, das hier steht.
        var t = await _a.Actions.CreateAsync(
            _a.Rudi,
            new CreateTournamentRequest("Doppelrunde", Discipline: Discipline.Doubles, Participants: ["Anna / Tom", "Rudi / Max"]),
            CancellationToken.None);

        var modell = new Modell([new TextContent("Alles klar.")]);
        await Lauf(_a.Agent(modell), new ChatRequest("Wer spielt mit?", "sitzung-11", t.Id));

        var kontext = modell.Anfragen.Single().Single().Text;
        Assert.Contains("Doppel", kontext);
        Assert.Contains("2 Teams", kontext);
    }

    [Fact]
    public async Task Ein_verschwundenes_Turnier_heisst_geloescht()
    {
        var modell = new Modell([new TextContent("Dann eben neu.")]);

        await Lauf(_a.Agent(modell), new ChatRequest("Und nun?", "sitzung-10", Guid.NewGuid()));

        Assert.Contains("Aktuelles Turnier: keines mehr (gelöscht)", modell.Anfragen.Single().Single().Text);
    }

    // --- Werkzeug ----------------------------------------------------------

    private static FunctionCallContent Aufruf(string name, object argumente, string id = "ruf-1") =>
        new(id, name, JsonSerializer.SerializeToElement(argumente).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value));

    private TournamentAgent OhneModell()
    {
        var leer = new ConfigurationBuilder().Build();
        var zugang = new ModelAccess(Options.Create(new AgentOptions { Model = "gpt-5" }), leer, NullLoggerFactory.Instance);

        return new TournamentAgent(_a.Tools, _a.Actions, _a.Store, zugang, TimeProvider.System, NullLogger<TournamentAgent>.Instance);
    }

    private async Task<List<ChatEvent>> Lauf(TournamentAgent agent, ChatRequest anfrage)
    {
        var ereignisse = new List<ChatEvent>();

        await foreach (var ereignis in agent.RunAsync(anfrage, _a.Rudi, "https://matchday.test", CancellationToken.None))
        {
            ereignisse.Add(ereignis);
        }

        return ereignisse;
    }

    private static JsonElement Daten(ChatEvent ereignis) => JsonSerializer.SerializeToElement(ereignis.Data);
}
