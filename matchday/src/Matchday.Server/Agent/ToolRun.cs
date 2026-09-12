using System.Text.Json;
using Matchday.Server.Api;
using Matchday.Server.Storage;
using Microsoft.Extensions.AI;

namespace Matchday.Server.Agent;

/// <summary>
/// Ein gelaufener Werkzeugaufruf: was das Modell gerufen hat, was es als
/// Antwort gelesen hat, und was davon in die Oberfläche geht.
/// </summary>
public sealed record ToolStep(string Name, JsonElement Input, string Result, ToolOutcome Outcome);

/// <summary>
/// Die Werkzeuge einer Anfrage. Sie kennen den Aufrufer und das aktuelle
/// Turnier, rufen dieselben <see cref="TournamentActions"/> wie die HTTP-API
/// und schreiben mit, was gelaufen ist: das Modell bekommt nur den Text, den
/// Rest nimmt der Ereignisstrom mit (ADR-0017).
/// </summary>
public sealed class ToolRun
{
    private readonly AgentTools _tools;
    private readonly Actor _actor;
    private readonly string _baseUrl;
    // Das Framework ruft Werkzeuge der Reihe nach, nicht gleichzeitig. Das
    // Schloss steht trotzdem: die Reihe ist eine Vorgabe, keine Zusage.
    private readonly Lock _gate = new();
    private readonly List<ToolStep> _steps = [];
    private int _reported;

    public ToolRun(AgentTools tools, Actor actor, string baseUrl, Guid? tournamentId)
    {
        _tools = tools;
        _actor = actor;
        _baseUrl = baseUrl;
        TournamentId = tournamentId;
        Functions = [.. AgentTools.Definitions.Select(definition => (AITool)new ToolFunction(definition, this))];
    }

    /// <summary>Das Turnier, um das es gerade geht — Werkzeuge dürfen es wechseln.</summary>
    public Guid? TournamentId { get; private set; }

    public IList<AITool> Functions { get; }

    /// <summary>
    /// Der nächste Schritt, den die Oberfläche noch nicht gesehen hat — oder
    /// nichts, wenn zu diesem Ergebnis keiner gelaufen ist. Ergebnisse kommen
    /// im Strom in derselben Reihenfolge zurück wie die Aufrufe, darum genügt
    /// die Reihe.
    /// </summary>
    public ToolStep? NextStep()
    {
        lock (_gate)
        {
            return _reported < _steps.Count ? _steps[_reported++] : null;
        }
    }

    internal async Task<string> ExecuteAsync(string name, JsonElement input, CancellationToken ct)
    {
        var outcome = await _tools.ExecuteAsync(name, input, _actor, TournamentId, _baseUrl, ct);

        if (outcome.TournamentId is { } switched)
        {
            TournamentId = switched;
        }
        else if (name == "delete_tournament" && !outcome.IsError)
        {
            TournamentId = null;
        }

        // Ein zurückgewiesenes Werkzeug ist kein Absturz: das Modell soll den
        // Grund lesen und nachfragen, nicht abbrechen.
        var result = outcome.IsError ? $"Fehler: {outcome.ResultForModel}" : outcome.ResultForModel;

        lock (_gate)
        {
            _steps.Add(new ToolStep(name, input, result, outcome));
        }

        return result;
    }
}

/// <summary>
/// Ein Werkzeug, wie das Framework es erwartet — mit dem Schema, das in
/// <see cref="AgentTools.Definitions"/> steht. Die Eingabe bleibt JSON: die
/// Werkzeuge lesen sie selbst, es gibt keine zweite Beschreibung der Felder.
/// </summary>
internal sealed class ToolFunction(ToolDefinition definition, ToolRun run) : AIFunction
{
    private static readonly JsonSerializerOptions Json = TournamentStore.Json;

    public override string Name => definition.Name;

    public override string Description => definition.Description;

    public override JsonElement JsonSchema { get; } = JsonSerializer.SerializeToElement(definition.InputSchema, Json);

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
        await run.ExecuteAsync(definition.Name, ToJson(arguments), cancellationToken);

    private static JsonElement ToJson(AIFunctionArguments arguments) =>
        JsonSerializer.SerializeToElement(arguments.ToDictionary(argument => argument.Key, argument => argument.Value), Json);
}
