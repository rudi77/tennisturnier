using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Matchday.Server.Api;
using Matchday.Server.Storage;
using Microsoft.Extensions.Options;

namespace Matchday.Server.Agent;

/// <summary>Ein Ereignis auf dem Weg zur Oberfläche: Text, Werkzeugaufruf, Widget, Ende.</summary>
public sealed record ChatEvent(string Type, object Data);

public sealed record ChatRequest(string Message, string? SessionId = null, Guid? TournamentId = null);

/// <summary>
/// Die Werkzeugschleife: Modell fragen, Werkzeuge ausführen, Ergebnisse
/// zurückgeben, bis das Modell antwortet. Selbst geschrieben, weil jeder
/// Schritt als Ereignis an die Oberfläche gehen soll (ADR-0016).
/// </summary>
public sealed class TournamentAgent(
    AgentTools tools,
    TournamentActions actions,
    TournamentStore store,
    IOptions<AgentOptions> options,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<TournamentAgent> log)
{
    private static readonly JsonSerializerOptions Json = TournamentStore.Json;

    private string? ApiKey =>
        configuration["Anthropic:ApiKey"] is { Length: > 0 } key ? key : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public async Task<ChatSession> LoadSessionAsync(string sessionId, Actor actor, CancellationToken ct)
    {
        var json = await store.FindSessionJsonAsync(sessionId, actor.ClientId, ct);
        return json is null
            ? new ChatSession { Id = sessionId, ClientId = actor.ClientId }
            : JsonSerializer.Deserialize<ChatSession>(json, Json)!;
    }

    public async IAsyncEnumerable<ChatEvent> RunAsync(
        ChatRequest request,
        Actor actor,
        string baseUrl,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var session = await LoadSessionAsync(request.SessionId ?? Guid.NewGuid().ToString("N"), actor, ct);
        yield return new ChatEvent("session", new { sessionId = session.Id });

        if (request.TournamentId is { } requested)
        {
            session.TournamentId = requested;
        }

        if (!IsConfigured)
        {
            yield return new ChatEvent("error", new { message = "Kein Modellschlüssel konfiguriert (ANTHROPIC_API_KEY). Die Widgets funktionieren trotzdem." });
            yield break;
        }

        session.Messages.Add(new StoredMessage
        {
            Role = "user",
            Blocks = [new StoredBlock { Kind = BlockKind.Text, Text = await ContextFor(session, actor, ct) + "\n\n" + request.Message }],
        });

        var client = new AnthropicClient { ApiKey = ApiKey };
        var settings = options.Value;

        for (var round = 0; round <= settings.MaxToolRounds; round++)
        {
            var (response, failure) = await AskAsync(client, session, settings, ct);

            if (response is null)
            {
                log.LogError(failure, "Modellaufruf fehlgeschlagen");
                yield return new ChatEvent("error", new { message = "Das Modell ist gerade nicht erreichbar. Die Widgets funktionieren trotzdem." });
                yield break;
            }

            var assistant = new StoredMessage { Role = "assistant" };
            var toolUses = new List<(string Id, string Name, JsonElement Input)>();

            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var text))
                {
                    assistant.Blocks.Add(new StoredBlock { Kind = BlockKind.Text, Text = text.Text });
                    yield return new ChatEvent("text", new { text = text.Text });
                }
                else if (block.TryPickThinking(out var thinking))
                {
                    assistant.Blocks.Add(new StoredBlock { Kind = BlockKind.Thinking, Text = thinking.Thinking, Signature = thinking.Signature });
                }
                else if (block.TryPickToolUse(out var toolUse))
                {
                    var input = JsonSerializer.SerializeToElement(toolUse.Input, Json);
                    assistant.Blocks.Add(new StoredBlock { Kind = BlockKind.ToolUse, Id = toolUse.ID, Name = toolUse.Name, Input = input });
                    toolUses.Add((toolUse.ID, toolUse.Name, input));
                }
            }

            session.Messages.Add(assistant);

            if (response.StopReason == "refusal")
            {
                yield return new ChatEvent("error", new { message = "Das Modell hat diese Anfrage abgelehnt." });
                break;
            }

            if (toolUses.Count == 0 || response.StopReason != "tool_use")
            {
                break;
            }

            var results = new StoredMessage { Role = "user" };

            foreach (var (id, name, input) in toolUses)
            {
                yield return new ChatEvent("tool", new { name, input });

                var outcome = await tools.ExecuteAsync(name, input, actor, session.TournamentId, baseUrl, ct);

                if (outcome.TournamentId is { } switched)
                {
                    session.TournamentId = switched;
                }
                else if (name == "delete_tournament" && !outcome.IsError)
                {
                    session.TournamentId = null;
                }

                if (outcome.Widget is not null)
                {
                    yield return new ChatEvent("widget", new { widget = outcome.Widget, data = outcome.WidgetData, tournamentId = session.TournamentId });
                }

                results.Blocks.Add(new StoredBlock
                {
                    Kind = BlockKind.ToolResult,
                    ToolUseId = id,
                    Text = outcome.ResultForModel,
                    IsError = outcome.IsError,
                    Widget = outcome.Widget,
                });
            }

            session.Messages.Add(results);
        }

        await store.SaveSessionJsonAsync(session.Id, session.ClientId, JsonSerializer.Serialize(session, Json), ct);
        yield return new ChatEvent("done", new { sessionId = session.Id, tournamentId = session.TournamentId });
    }

    private async Task<(Message? Response, Exception? Failure)> AskAsync(AnthropicClient client, ChatSession session, AgentOptions settings, CancellationToken ct)
    {
        try
        {
            return (await client.Messages.Create(BuildParams(session, settings), cancellationToken: ct), null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (null, e);
        }
    }

    // --- Aufbau der Anfrage ------------------------------------------------

    private MessageCreateParams BuildParams(ChatSession session, AgentOptions settings) => new()
    {
        Model = settings.Model,
        MaxTokens = settings.MaxTokens,
        System = new List<TextBlockParam>
        {
            new() { Text = SystemPrompt, CacheControl = new CacheControlEphemeral() },
        },
        OutputConfig = new OutputConfig { Effort = EffortOf(settings.Effort) },
        Tools = [.. AgentTools.Definitions.Select(ToSdkTool)],
        Messages = [.. session.Messages.Select(ToSdkMessage)],
    };

    private static Effort EffortOf(string effort) => effort.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };

    private static ToolUnion ToSdkTool(ToolDefinition definition)
    {
        var schema = JsonSerializer.SerializeToElement(definition.InputSchema, Json);
        var properties = schema.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToList();

        return new Tool
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = new() { Properties = properties, Required = required },
        };
    }

    private static MessageParam ToSdkMessage(StoredMessage message)
    {
        var content = new List<ContentBlockParam>();

        foreach (var block in message.Blocks)
        {
            switch (block.Kind)
            {
                case BlockKind.Text:
                    content.Add(new TextBlockParam { Text = block.Text ?? "" });
                    break;
                case BlockKind.Thinking:
                    content.Add(new ThinkingBlockParam { Thinking = block.Text ?? "", Signature = block.Signature ?? "" });
                    break;
                case BlockKind.ToolUse:
                    content.Add(new ToolUseBlockParam
                    {
                        ID = block.Id!,
                        Name = block.Name!,
                        Input = block.Input!.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
                    });
                    break;
                case BlockKind.ToolResult:
                    content.Add(new ToolResultBlockParam { ToolUseID = block.ToolUseId!, Content = block.Text ?? "", IsError = block.IsError });
                    break;
            }
        }

        return new MessageParam { Role = message.Role == "assistant" ? Role.Assistant : Role.User, Content = content };
    }

    /// <summary>Was sich je Runde ändert, steht in der Nachricht, nicht im System-Prompt — der bleibt im Cache.</summary>
    private async Task<string> ContextFor(ChatSession session, Actor actor, CancellationToken ct)
    {
        var now = clock.GetLocalNow();
        var mine = await actions.ListMineAsync(actor, ct);
        var lines = new List<string>
        {
            "<context>",
            $"Heute: {now:yyyy-MM-dd} ({now.DayOfWeek})",
        };

        lines.Add(mine.Count == 0
            ? "Turniere in diesem Browser: keine"
            : "Turniere in diesem Browser: " + string.Join("; ", mine.Select(t => $"„{t.Name}“ (id {t.Id}, {t.State}, {t.Participants.Count} Teilnehmer)")));

        if (session.TournamentId is { } current)
        {
            var t = mine.FirstOrDefault(x => x.Id == current) ?? await store.FindAsync(current, ct);
            lines.Add(t is null ? "Aktuelles Turnier: keines mehr (gelöscht)" : $"Aktuelles Turnier: „{t.Name}“ (id {t.Id})");
        }
        else
        {
            lines.Add("Aktuelles Turnier: keines");
        }

        lines.Add("</context>");
        return string.Join("\n", lines);
    }

    internal const string SystemPrompt = """
        Du bist MATCHDAY, der Assistent für ein Tennisturnier unter Freunden. Du führst ein Turnier von der Idee bis zum Finale: anlegen, Teilnehmer eintragen, auslosen, Ergebnisse eintragen, Links zum Mitschauen geben.

        Regeln:
        - Handle mit den Werkzeugen. Du entscheidest nie selbst über Auslosung, Tabelle oder die Gültigkeit eines Satzes — das tut die Anwendung. Erfinde keine Ergebnisse und keine Paarungen.
        - Weist ein Werkzeug eine Eingabe zurück, sag in einem Satz warum, und frag nach, was fehlt.
        - Auslosen, Auslosung zurücknehmen und Löschen sind unumkehrbar. Rufe diese Werkzeuge nur, wenn der Benutzer es in seiner letzten Nachricht ausdrücklich verlangt oder bestätigt hat. Sonst frag kurz nach.
        - Zum Anlegen genügt ein Name. Frag nicht nach Datum, Ort oder Format, wenn der Benutzer nichts dazu sagt.
        - Jedes Werkzeugergebnis erscheint als Widget in der Oberfläche. Wiederhole deshalb keine Listen, Brackets oder Tabellen im Text. Antworte in ein bis drei kurzen Sätzen: was passiert ist und was der nächste Schritt sein könnte.
        - Sprich Deutsch, du, freundlich, ohne Floskeln. Namen so schreiben, wie der Benutzer sie schreibt.
        - Ergebnisse: Der Benutzer sagt etwa „Rudi hat gegen Max 6:4 3:6 10:8 gewonnen“. Sätze immer aus Sicht des Siegers eintragen. Sagt er ein Ergebnis aus Sicht des Verlierers („Max hat 4:6 verloren“), dreh es um.
        - Das aktuelle Turnier steht im Kontext. Gibt es keines und der Benutzer redet von einem Turnier, nimm das passende aus der Liste (get_tournament) oder frag, welches gemeint ist.
        - Relative Datumsangaben („Samstag“, „nächste Woche“) rechnest du mit dem Datum aus dem Kontext in YYYY-MM-DD um.
        """;
}
