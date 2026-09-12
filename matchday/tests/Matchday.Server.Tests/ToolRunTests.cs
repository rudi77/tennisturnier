using System.Text.Json;
using Matchday.Server.Agent;
using Microsoft.Extensions.AI;

namespace Matchday.Server.Tests;

/// <summary>
/// Die Werkzeuge, wie das Framework sie sieht: ein Name, ein Schema, ein
/// Rückgabetext. Was für die Oberfläche abfällt, bleibt daneben liegen.
/// </summary>
public sealed class ToolRunTests : IDisposable
{
    private readonly Aufbau _a = new();

    public void Dispose() => _a.Dispose();

    [Fact]
    public void Jedes_Werkzeug_kommt_mit_Namen_und_Schema_durch()
    {
        var lauf = Lauf();

        Assert.Equal(AgentTools.Definitions.Count, lauf.Functions.Count);

        foreach (var definition in AgentTools.Definitions)
        {
            var funktion = Assert.IsAssignableFrom<AIFunction>(lauf.Functions.Single(f => f.Name == definition.Name));

            Assert.Equal(definition.Description, funktion.Description);
            Assert.Equal("object", funktion.JsonSchema.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, funktion.JsonSchema.GetProperty("properties").ValueKind);
        }
    }

    [Fact]
    public async Task Ein_Werkzeug_legt_an_und_merkt_sich_das_Widget()
    {
        var lauf = Lauf();

        var text = await Rufen(lauf, "create_tournament", new AIFunctionArguments { ["name"] = "Sommercup" });

        Assert.Contains("Sommercup", text);
        Assert.NotNull(lauf.TournamentId);

        var schritt = lauf.NextStep();
        Assert.NotNull(schritt);
        Assert.Equal("create_tournament", schritt.Name);
        Assert.Equal("Sommercup", schritt.Input.GetProperty("name").GetString());
        Assert.Equal(AgentTools.WidgetTournament, schritt.Outcome.Widget);
        Assert.False(schritt.Outcome.IsError);

        // Zweimal gibt es denselben Schritt nicht: die Oberfläche hat ihn gesehen.
        Assert.Null(lauf.NextStep());
    }

    [Fact]
    public async Task Ergebnisse_kommen_in_der_Reihenfolge_der_Aufrufe()
    {
        var lauf = Lauf();
        await Rufen(lauf, "create_tournament", new AIFunctionArguments { ["name"] = "Sommercup" });
        await Rufen(lauf, "add_participants", new AIFunctionArguments { ["names"] = new[] { "Rudi", "Max" } });
        await Rufen(lauf, "share_links", new AIFunctionArguments());

        var schritte = new[] { lauf.NextStep(), lauf.NextStep(), lauf.NextStep() };

        Assert.Equal(
            new[] { "create_tournament", "add_participants", "share_links" },
            schritte.Select(s => s!.Name));
        Assert.Equal(
            new[] { AgentTools.WidgetTournament, AgentTools.WidgetParticipants, AgentTools.WidgetShare },
            schritte.Select(s => s!.Outcome.Widget!));
    }

    [Fact]
    public async Task Ein_zurueckgewiesenes_Werkzeug_sagt_Fehler()
    {
        var lauf = Lauf();

        var text = await Rufen(lauf, "create_tournament", new AIFunctionArguments { ["name"] = "  " });

        Assert.StartsWith("Fehler: ", text);
        Assert.True(lauf.NextStep()?.Outcome.IsError);
        Assert.Null(lauf.TournamentId);
    }

    [Fact]
    public async Task Ein_geloeschtes_Turnier_ist_nicht_mehr_das_aktuelle()
    {
        var lauf = Lauf();
        await Rufen(lauf, "create_tournament", new AIFunctionArguments { ["name"] = "Sommercup" });
        var id = lauf.TournamentId;

        await Rufen(lauf, "delete_tournament", new AIFunctionArguments { ["tournamentId"] = id!.Value.ToString() });

        Assert.Null(lauf.TournamentId);
    }

    [Fact]
    public async Task Ein_gescheitertes_Loeschen_laesst_das_Turnier_stehen()
    {
        var lauf = Lauf();
        await Rufen(lauf, "create_tournament", new AIFunctionArguments { ["name"] = "Sommercup" });
        var id = lauf.TournamentId;

        var text = await Rufen(lauf, "delete_tournament", new AIFunctionArguments { ["tournamentId"] = Guid.NewGuid().ToString() });

        Assert.StartsWith("Fehler: ", text);
        Assert.Equal(id, lauf.TournamentId);
    }

    [Fact]
    public async Task Ein_unbekanntes_Werkzeug_ist_ein_Fehler_und_kein_Absturz()
    {
        var lauf = Lauf();
        var funktion = (AIFunction)lauf.Functions[0];

        // Der Weg über das Framework ginge nicht: dort gibt es nur die zwölf.
        var text = await lauf.ExecuteAsync("fliegen", JsonSerializer.SerializeToElement(new { }), CancellationToken.None);

        Assert.StartsWith("Fehler: Unbekanntes Werkzeug", text);
        Assert.Equal("list_tournaments", funktion.Name);
    }

    private ToolRun Lauf() => new(_a.Tools, _a.Rudi, "https://matchday.test", null);

    private static async Task<string> Rufen(ToolRun lauf, string name, AIFunctionArguments eingabe)
    {
        var funktion = (AIFunction)lauf.Functions.Single(f => f.Name == name);
        return (string)(await funktion.InvokeAsync(eingabe, CancellationToken.None))!;
    }
}
