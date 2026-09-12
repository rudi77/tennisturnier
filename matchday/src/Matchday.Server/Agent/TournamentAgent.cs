using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Matchday.Domain;
using Matchday.Server.Api;
using Matchday.Server.Storage;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Matchday.Server.Agent;

/// <summary>Ein Ereignis auf dem Weg zur Oberfläche: Text, Werkzeugaufruf, Widget, Ende.</summary>
public sealed record ChatEvent(string Type, object Data);

public sealed record ChatRequest(string Message, string? SessionId = null, Guid? TournamentId = null);

/// <summary>
/// Das Gespräch mit dem Modell. Die Werkzeugschleife führt das Microsoft Agent
/// Framework; hier wird sie nur mitgelesen, damit jeder Schritt als Ereignis an
/// die Oberfläche geht: Text, Werkzeugaufruf, Widget (ADR-0017).
/// </summary>
public sealed class TournamentAgent(
    AgentTools tools,
    TournamentActions actions,
    TournamentStore store,
    ModelAccess model,
    TimeProvider clock,
    ILogger<TournamentAgent> log)
{
    private static readonly JsonSerializerOptions Json = TournamentStore.Json;

    public bool IsConfigured => model.IsConfigured;

    /// <summary>Leer, wenn der Zugang steht; sonst der Satz, der sagt, was fehlt.</summary>
    public string Missing => model.Missing;

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

        if (!model.IsConfigured)
        {
            yield return new ChatEvent("error", new { message = model.Missing });
            yield break;
        }

        session.Messages.Add(new StoredMessage
        {
            Role = "user",
            Blocks = [new StoredBlock { Kind = BlockKind.Text, Text = await ContextFor(session, actor, ct) + "\n\n" + request.Message }],
        });

        var run = new ToolRun(tools, actor, baseUrl, session.TournamentId);
        var agent = model.CreateAgent(SystemPrompt, run.Functions);

        var updates = new List<AgentResponseUpdate>();
        var steps = new Dictionary<string, ToolStep>();
        var text = new StringBuilder();
        var failed = false;

        var stream = agent.RunStreamingAsync([.. session.Messages.Select(ToChatMessage)], cancellationToken: ct);
        await using var updateStream = stream.GetAsyncEnumerator(ct);

        while (true)
        {
            AgentResponseUpdate? update;

            try
            {
                update = await updateStream.MoveNextAsync() ? updateStream.Current : null;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Modellaufruf fehlgeschlagen");
                failed = true;
                break;
            }

            if (update is null)
            {
                break;
            }

            updates.Add(update);

            foreach (var chatEvent in EventsFrom(update, run, session, steps, text))
            {
                yield return chatEvent;
            }
        }

        foreach (var chatEvent in Rest(text))
        {
            yield return chatEvent;
        }

        session.TournamentId = run.TournamentId;

        if (failed)
        {
            // Bruchstücke nicht aufheben: ein Werkzeugaufruf ohne Ergebnis
            // würde die nächste Runde stolpern lassen. Was die Werkzeuge in der
            // Datenbank getan haben, steht dort ohnehin.
            await store.SaveSessionJsonAsync(session.Id, session.ClientId, JsonSerializer.Serialize(session, Json), ct);
            yield return new ChatEvent("error", new { message = "Das Modell ist gerade nicht erreichbar. Die Widgets funktionieren trotzdem." });
            yield break;
        }

        foreach (var message in updates.ToAgentResponse().Messages)
        {
            var stored = ToStored(message, steps);

            if (stored.Blocks.Count > 0)
            {
                session.Messages.Add(stored);
            }
        }

        await store.SaveSessionJsonAsync(session.Id, session.ClientId, JsonSerializer.Serialize(session, Json), ct);
        yield return new ChatEvent("done", new { sessionId = session.Id, tournamentId = session.TournamentId });
    }

    // --- Vom Strom zu den Ereignissen --------------------------------------

    /// <summary>
    /// Was ein Stück des Stroms für die Oberfläche bedeutet. Text wird gesammelt
    /// und erst herausgegeben, wenn er fertig ist: die Oberfläche macht aus
    /// jedem Textereignis eine Sprechblase, und Wort für Wort wären es hunderte.
    /// </summary>
    private static List<ChatEvent> EventsFrom(
        AgentResponseUpdate update,
        ToolRun run,
        ChatSession session,
        Dictionary<string, ToolStep> steps,
        StringBuilder text)
    {
        var events = new List<ChatEvent>();

        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 } part:
                    text.Append(part.Text);
                    break;

                case FunctionCallContent:
                    events.AddRange(Rest(text));
                    break;

                case FunctionResultContent result:
                    events.AddRange(Rest(text));

                    // Kein Schritt heißt: dieser Aufruf ist nicht gelaufen, weil
                    // die Runden aufgebraucht waren. Dann gibt es nichts zu zeigen.
                    if (run.NextStep() is { } step)
                    {
                        steps[result.CallId] = step;
                        session.TournamentId = run.TournamentId;
                        events.Add(new ChatEvent("tool", new { name = step.Name, input = step.Input }));

                        if (step.Outcome.Widget is not null)
                        {
                            events.Add(new ChatEvent("widget", new { widget = step.Outcome.Widget, data = step.Outcome.WidgetData, tournamentId = session.TournamentId }));
                        }
                    }

                    break;
            }
        }

        return events;
    }

    /// <summary>Der gesammelte Text, falls noch etwas aussteht.</summary>
    private static List<ChatEvent> Rest(StringBuilder text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var events = new List<ChatEvent> { new("text", new { text = text.ToString() }) };
        text.Clear();
        return events;
    }

    // --- Zwischen Speicherform und Framework -------------------------------

    private static ChatMessage ToChatMessage(StoredMessage message)
    {
        var contents = new List<AIContent>();

        foreach (var block in message.Blocks)
        {
            switch (block.Kind)
            {
                case BlockKind.Text:
                    contents.Add(new TextContent(block.Text ?? string.Empty));
                    break;
                case BlockKind.Thinking:
                    contents.Add(new TextReasoningContent(block.Text ?? string.Empty) { ProtectedData = block.Signature });
                    break;
                case BlockKind.ToolUse:
                    contents.Add(new FunctionCallContent(block.Id ?? string.Empty, block.Name ?? string.Empty, Arguments(block.Input)));
                    break;
                case BlockKind.ToolResult:
                    contents.Add(new FunctionResultContent(block.ToolUseId ?? string.Empty, block.Text ?? string.Empty));
                    break;
            }
        }

        return new ChatMessage(RoleOf(message), contents);
    }

    /// <summary>
    /// Ein Werkzeugergebnis trägt seine eigene Rolle — auch in Sitzungen, die
    /// noch aus der Zeit davor stammen und es beim Benutzer stehen haben.
    /// </summary>
    private static ChatRole RoleOf(StoredMessage message) =>
        message.Blocks.Any(block => block.Kind == BlockKind.ToolResult) ? ChatRole.Tool
            : message.Role == "assistant" ? ChatRole.Assistant
            : ChatRole.User;

    private static Dictionary<string, object?> Arguments(JsonElement? input) =>
        input is { ValueKind: JsonValueKind.Object } element
            ? element.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone())
            : [];

    private static StoredMessage ToStored(ChatMessage message, Dictionary<string, ToolStep> steps)
    {
        var stored = new StoredMessage { Role = message.Role.Value };

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 } text:
                    stored.Blocks.Add(new StoredBlock { Kind = BlockKind.Text, Text = text.Text });
                    break;

                case TextReasoningContent reasoning:
                    stored.Blocks.Add(new StoredBlock { Kind = BlockKind.Thinking, Text = reasoning.Text, Signature = reasoning.ProtectedData });
                    break;

                // Aufruf und Ergebnis gehören zusammen in den Verlauf oder gar
                // nicht: eine offene Frage — oder eine Antwort ohne Frage —
                // würde die nächste Runde beim Modell abgewiesen werden. Offen
                // bleibt, was die Runden nicht mehr geschafft haben, und was das
                // Modell sich an Werkzeugen ausgedacht hat.
                case FunctionCallContent call when steps.ContainsKey(call.CallId):
                    stored.Blocks.Add(new StoredBlock
                    {
                        Kind = BlockKind.ToolUse,
                        Id = call.CallId,
                        Name = call.Name,
                        Input = JsonSerializer.SerializeToElement(call.Arguments, Json),
                    });
                    break;

                case FunctionResultContent result when steps.TryGetValue(result.CallId, out var step):
                    stored.Blocks.Add(new StoredBlock
                    {
                        Kind = BlockKind.ToolResult,
                        ToolUseId = result.CallId,
                        Text = step.Result,
                        IsError = step.Outcome.IsError,
                        Widget = step.Outcome.Widget,
                    });
                    break;
            }
        }

        return stored;
    }

    /// <summary>Was sich je Runde ändert, steht in der Nachricht, nicht in den Anweisungen.</summary>
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
            : "Turniere in diesem Browser: " + string.Join("; ", mine.Select(Describe)));

        if (session.TournamentId is { } current)
        {
            var t = mine.FirstOrDefault(x => x.Id == current) ?? await store.FindAsync(current, ct);
            lines.Add(t is null ? "Aktuelles Turnier: keines mehr (gelöscht)" : $"Aktuelles Turnier: {Describe(t)}");
        }
        else
        {
            lines.Add("Aktuelles Turnier: keines");
        }

        lines.Add("</context>");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Ein Turnier in einer Zeile. Die Disziplin gehört dazu: Wer sie erst mit
    /// get_tournament erführe, trüge im Doppel zwei Spieler als zwei Teams ein
    /// und bekäme eine Absage für etwas, das im Kontext stehen könnte.
    /// </summary>
    private static string Describe(Tournament t) =>
        $"„{t.Name}“ (id {t.Id}, {AgentTools.DisciplineText(t.Discipline)}, {AgentTools.ModeText(t.Mode)}, " +
        $"{AgentTools.StateText(t.State)}, {t.Participants.Count} {(t.Discipline == Discipline.Doubles ? "Teams" : "Teilnehmer")})";

    /// <summary>
    /// Die Anweisungen. Sie sagen, wie der Agent handelt; was er über die
    /// Anwendung und über Tennis weiß, steht in <see cref="Knowledge"/> und
    /// hängt darunter.
    /// </summary>
    internal static readonly string SystemPrompt = """
        Du bist MATCHDAY, der Assistent für ein Tennisturnier unter Freunden. Du kannst zwei Dinge: ein Turnier von der Idee bis zum Finale führen — anlegen, Teilnehmer eintragen, auslosen, Ergebnisse eintragen, Links zum Mitschauen geben — und Fragen dazu beantworten: zur Anwendung, zum Ablauf, zu den Modi und zu den Spielregeln.

        Handeln:
        - Handle mit den Werkzeugen. Du entscheidest nie selbst über Auslosung, Tabelle oder die Gültigkeit eines Satzes — das tut die Anwendung. Erfinde keine Ergebnisse und keine Paarungen.
        - Weist ein Werkzeug eine Eingabe zurück, sag in einem Satz warum, und frag nach, was fehlt.
        - Auslosen, Auslosung zurücknehmen und Löschen sind unumkehrbar. Rufe diese Werkzeuge nur, wenn der Benutzer es in seiner letzten Nachricht ausdrücklich verlangt oder bestätigt hat. Sonst frag kurz nach.
        - Zum Anlegen genügt ein Name. Frag nicht nach Datum, Ort oder Format, wenn der Benutzer nichts dazu sagt. Sagt er „Doppel“, leg es als Doppel an (discipline=Doubles).
        - Jedes Werkzeugergebnis erscheint als Widget in der Oberfläche. Wiederhole deshalb keine Listen, Brackets oder Tabellen im Text. Antworte in ein bis drei kurzen Sätzen: was passiert ist und was der nächste Schritt sein könnte.
        - Ergebnisse: Der Benutzer sagt etwa „Rudi hat gegen Max 6:4 3:6 10:8 gewonnen“. Sätze immer aus Sicht des Siegers eintragen. Sagt er ein Ergebnis aus Sicht des Verlierers („Max hat 4:6 verloren“), dreh es um.
        - Im Doppel ist ein Teilnehmer ein Team aus zwei Spielern: „Anna / Tom“. Für ein Ergebnis genügt je Team ein Spielername.
        - Das aktuelle Turnier steht im Kontext. Gibt es keines und der Benutzer redet von einem Turnier, nimm das passende aus der Liste (get_tournament) oder frag, welches gemeint ist.
        - Relative Datumsangaben („Samstag“, „nächste Woche“) rechnest du mit dem Datum aus dem Kontext in YYYY-MM-DD um.
        - Der Benutzer kann alles auch selbst über die Widgets tun. Wundere dich nicht über Teilnehmer, Ergebnisse oder Turniere, die du nicht eingetragen hast — hol dir den Stand mit get_tournament, statt ihm zu widersprechen.

        Auskunft geben:
        - Fragen zur Anwendung, zum Ablauf, zu den Modi, zum Satzformat und zu den Tennisregeln beantwortest du aus dem Wissen unten, ohne ein Werkzeug zu rufen. Nichts davon verändert ein Turnier.
        - Zum Erklären darfst du mehr Platz nehmen als drei Sätze — eine kurze Liste, wo sie hilft. Bleib bei dem, was gefragt ist.
        - Was im Wissen nicht steht, erfindest du nicht. Kann die Anwendung etwas nicht, sag das in einem Satz und nenne, was sie stattdessen kann.
        - Fragt jemand, wie er selbst etwas tun kann, beschreib den Weg über die Widgets — und biete an, es gleich für ihn zu tun.

        Sprich Deutsch, du, freundlich, ohne Floskeln. Namen so schreiben, wie der Benutzer sie schreibt.
        """ + "\n\n" + Knowledge.Text;
}
