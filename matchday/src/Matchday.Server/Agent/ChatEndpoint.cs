using Matchday.Server.Api;

namespace Matchday.Server.Agent;

public static class ChatEndpoint
{
    public static void MapChat(this IEndpointRouteBuilder app)
    {
        // Nicht nur ob, sondern was: ModelAccess unterscheidet drei Fälle, und
        // ohne den Satz bliebe der Oberfläche nur Raten.
        app.MapGet("/api/chat/status", (TournamentAgent agent) =>
            Results.Ok(new { configured = agent.IsConfigured, missing = agent.Missing }));

        app.MapGet("/api/chat/{sessionId}", async (HttpContext http, TournamentAgent agent, string sessionId, CancellationToken ct) =>
            Results.Ok(Transcript(await agent.LoadSessionAsync(sessionId, Endpoints.ActorOf(http), ct))));

        // Ein Gespräch je Turnier. Gibt es noch keines, kommt ein leeres ohne
        // Id zurück — angelegt wird es mit der ersten Nachricht.
        app.MapGet("/api/chat/tournament/{tournamentId:guid}", async (HttpContext http, TournamentAgent agent, Guid tournamentId, CancellationToken ct) =>
        {
            var session = await agent.SessionForTournamentAsync(tournamentId, Endpoints.ActorOf(http), ct);
            return Results.Ok(session is null ? new TranscriptView(null, tournamentId, []) : Transcript(session));
        });

        app.MapDelete("/api/chat/{sessionId}", async (HttpContext http, TournamentAgent agent, string sessionId, CancellationToken ct) =>
            await agent.DeleteSessionAsync(sessionId, Endpoints.ActorOf(http), ct)
                ? Results.NoContent()
                : throw new NotFoundException("Dieses Gespräch gibt es nicht."));

        app.MapPost("/api/chat", async (HttpContext http, TournamentAgent agent, ChatRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 4000)
            {
                throw new BadHttpRequestException("Die Nachricht fehlt oder ist zu lang.");
            }

            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            await foreach (var chatEvent in agent.RunAsync(request, Endpoints.ActorOf(http), Endpoints.BaseUrl(http), ct))
            {
                await Endpoints.Write(http, chatEvent.Type, chatEvent.Data, ct);
            }
        });
    }

    /// <summary>Das Gespräch, wie die Oberfläche es nachlädt: Sprechblasen und Widgets, keine Werkzeugdetails.</summary>
    private static TranscriptView Transcript(ChatSession session) => new(
        session.Id,
        session.TournamentId,
        session.Messages
            .Select(m => new TranscriptMessage(
                m.Role,
                string.Join("\n", m.Blocks.Where(b => b.Kind == BlockKind.Text).Select(b => StripContext(b.Text ?? ""))),
                m.Blocks.Where(b => b.Widget is not null).Select(b => b.Widget!).ToList()))
            .Where(m => m.Text.Length > 0 || m.Widgets.Count > 0)
            .ToList());

    /// <summary>Der Kontextblock gehört dem Modell, nicht dem Menschen.</summary>
    private static string StripContext(string text)
    {
        var end = text.IndexOf("</context>", StringComparison.Ordinal);
        return end < 0 ? text : text[(end + "</context>".Length)..].TrimStart();
    }
}

public sealed record TranscriptMessage(string Role, string Text, IReadOnlyList<string> Widgets);

/// <summary>Ein Gespräch zum Nachladen. <c>Id</c> fehlt, solange noch keines geführt wurde.</summary>
public sealed record TranscriptView(string? Id, Guid? TournamentId, IReadOnlyList<TranscriptMessage> Messages);
