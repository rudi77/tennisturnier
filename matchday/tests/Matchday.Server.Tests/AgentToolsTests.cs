using System.Text.Json;
using Matchday.Domain;
using Matchday.Server.Agent;
using Matchday.Server.Api;

namespace Matchday.Server.Tests;

/// <summary>Die Werkzeuge ohne Modell: was das Modell rufen würde, rufen wir direkt.</summary>
public sealed class AgentToolsTests : IDisposable
{
    private readonly Aufbau _a = new();

    public void Dispose() => _a.Dispose();

    private static JsonElement In(object input) => JsonSerializer.SerializeToElement(input);

    private Task<ToolOutcome> Run(string name, object input, Guid? current = null) =>
        _a.Tools.ExecuteAsync(name, In(input), _a.Rudi, current, "https://matchday.test", CancellationToken.None);

    [Fact]
    public void Jedes_Werkzeug_hat_ein_gueltiges_Schema()
    {
        Assert.Equal(12, AgentTools.Definitions.Count);

        foreach (var tool in AgentTools.Definitions)
        {
            var schema = JsonSerializer.SerializeToElement(tool.InputSchema);
            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, schema.GetProperty("properties").ValueKind);
            Assert.NotEmpty(tool.Description);
        }
    }

    [Fact]
    public async Task Vom_Satz_zum_Sieger()
    {
        var anlegen = await Run("create_tournament", new
        {
            name = "Samstagsrunde",
            date = "2026-09-19",
            mode = "RoundRobin",
            bestOf = 1,
            participants = new[] { "Rudi", "Max", "Anna" },
        });

        Assert.False(anlegen.IsError);
        Assert.Equal(AgentTools.WidgetTournament, anlegen.Widget);
        Assert.Contains("Mitschau-Link: https://matchday.test/?t=", anlegen.ResultForModel);
        var id = anlegen.TournamentId!.Value;

        var liste = await Run("list_tournaments", new { });
        Assert.Equal(AgentTools.WidgetList, liste.Widget);
        Assert.Contains("Samstagsrunde", liste.ResultForModel);

        var mehr = await Run("add_participants", new { names = new[] { "Tom" } }, id);
        Assert.Equal(AgentTools.WidgetParticipants, mehr.Widget);
        Assert.Contains("Teilnehmer (4)", mehr.ResultForModel);

        var weniger = await Run("remove_participants", new { names = new[] { "tom", "Niemand" } }, id);
        Assert.Contains("Teilnehmer (3)", weniger.ResultForModel);
        Assert.Contains("Nicht gefunden: Niemand", weniger.ResultForModel);

        var zuFrueh = await Run("record_result", new { winner = "Rudi", loser = "Max", sets = new[] { new[] { 6, 4 } } }, id);
        Assert.True(zuFrueh.IsError);

        var los = await Run("draw", new { }, id);
        Assert.Equal(AgentTools.WidgetStandings, los.Widget);
        Assert.Contains("Runde 1", los.ResultForModel);

        var ergebnis = await Run("record_result", new { winner = "max", loser = "Rudi", sets = new[] { new[] { 7, 6, 4 } } }, id);
        Assert.False(ergebnis.IsError);
        Assert.Contains("→ Max 7:6 (4)", ergebnis.ResultForModel);

        var falsch = await Run("record_result", new { winner = "Anna", loser = "Rudi", sets = new[] { new[] { 6, 6 } } }, id);
        Assert.True(falsch.IsError);
        Assert.Contains("unentschieden", falsch.ResultForModel);

        var kampflos = await Run("record_result", new { winner = "Anna", loser = "Rudi", kind = "walkover" }, id);
        Assert.Contains("→ Anna kampflos", kampflos.ResultForModel);

        var zurueck = await Run("clear_result", new { nameA = "Rudi", nameB = "Anna" }, id);
        Assert.False(zurueck.IsError);
        Assert.Contains("(offen)", zurueck.ResultForModel);
        Assert.DoesNotContain("kampflos", zurueck.ResultForModel);

        var links = await Run("share_links", new { }, id);
        Assert.Equal(AgentTools.WidgetShare, links.Widget);
        Assert.Contains("?a=", links.ResultForModel);

        var ohneKontext = await Run("share_links", new { });
        Assert.True(ohneKontext.IsError);

        var weg = await Run("delete_tournament", new { tournamentId = id.ToString() });
        Assert.False(weg.IsError);
        Assert.Null(weg.TournamentId);
        Assert.Empty(await _a.Actions.ListMineAsync(_a.Rudi));
    }

    [Fact]
    public async Task Ergebnisse_werden_ueber_Namen_gefunden()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Cup"));
        await _a.Actions.AddParticipantsAsync(_a.Rudi, t.Id, ["Rudi", "Max", "Anna", "Tom"]);
        var gelost = await _a.Actions.DrawAsync(_a.Rudi, t.Id);

        var hf = gelost.Matches[0];
        var (gefunden, seite) = AgentTools.FindMatch(gelost, gelost.NameOf(hf.Side2), gelost.NameOf(hf.Side1));
        Assert.Equal(hf.Id, gefunden.Id);
        Assert.Equal(2, seite);

        Assert.Throws<DomainException>(() => AgentTools.FindMatch(gelost, "Rudi", "Rudi"));
        Assert.Throws<DomainException>(() => AgentTools.FindMatch(gelost, "Rudi", "Unbekannt"));
    }

    [Fact]
    public async Task Ein_Fremder_bekommt_eine_Absage_statt_einer_Ausnahme()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Cup"));
        var outcome = await _a.Tools.ExecuteAsync(
            "add_participants", In(new { names = new[] { "X" } }), _a.Fremder, t.Id, "https://matchday.test", CancellationToken.None);

        Assert.True(outcome.IsError);
        Assert.Contains("Verwalterlink", outcome.ResultForModel);

        var unbekannt = await Run("kaputt", new { });
        Assert.True(unbekannt.IsError);
    }

    [Fact]
    public async Task Aendern_ueber_das_Werkzeug()
    {
        var anlegen = await Run("create_tournament", new { name = "Cup" });
        var id = anlegen.TournamentId!.Value;

        var geaendert = await Run("update_tournament", new { name = "Herbstcup", date = "2026-10-03", location = "Wien", tiebreakAt = 4 }, id);
        Assert.False(geaendert.IsError);
        Assert.Contains("Herbstcup", geaendert.ResultForModel);
        Assert.Contains("Ort: Wien", geaendert.ResultForModel);
        Assert.Contains("bis 4", geaendert.ResultForModel);

        var entfernt = await Run("update_tournament", new { date = "", location = "" }, id);
        Assert.DoesNotContain("Ort:", entfernt.ResultForModel);

        var kaputt = await Run("update_tournament", new { date = "Samstag" }, id);
        Assert.True(kaputt.IsError);
    }
}
