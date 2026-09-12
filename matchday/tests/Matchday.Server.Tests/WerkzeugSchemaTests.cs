using System.Text.Json;
using Matchday.Server.Agent;

namespace Matchday.Server.Tests;

/// <summary>
/// Die Schemata der Werkzeuge, so wie das Modell sie zu sehen bekommt. Azure
/// prüft sie und weist die ganze Anfrage zurück, wenn eines nicht stimmt — mit
/// einem 400 mitten im Gespräch, nicht beim Start. Deshalb hier.
/// </summary>
public sealed class WerkzeugSchemaTests
{
    [Fact]
    public void Keine_Eigenschaft_ist_null()
    {
        // Der Fall, der es bis in den Betrieb geschafft hat: TournamentIdProperty
        // stand im Quelltext unter Definitions, und statische Initialisierer
        // laufen in Textreihenfolge. Beim Bau der Liste war das Feld noch null,
        // also schickte jedes betroffene Werkzeug „tournamentId": null.
        foreach (var werkzeug in AgentTools.Definitions)
        {
            var schema = JsonSerializer.SerializeToElement(werkzeug.InputSchema);
            var eigenschaften = schema.GetProperty("properties");

            foreach (var eigenschaft in eigenschaften.EnumerateObject())
            {
                Assert.False(
                    eigenschaft.Value.ValueKind == JsonValueKind.Null,
                    $"{werkzeug.Name}: „{eigenschaft.Name}“ ist null statt eines Schemas.");
            }
        }
    }

    [Fact]
    public void Jedes_Werkzeug_beschreibt_ein_Objekt()
    {
        foreach (var werkzeug in AgentTools.Definitions)
        {
            var schema = JsonSerializer.SerializeToElement(werkzeug.InputSchema);

            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, schema.GetProperty("properties").ValueKind);
        }
    }
}
