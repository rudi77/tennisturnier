using Matchday.Server.Api;

namespace Matchday.Server.Agent;

public static class ChatEndpoint
{
    public static void MapChat(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/chat/status", (TournamentAgent agent) => Results.Ok(new { configured = agent.IsConfigured }));

        app.MapGet("/api/chat/{sessionId}", async (HttpContext http, TournamentAgent agent, string sessionId, CancellationToken ct) =>
        {
            var session = await agent.LoadSessionAsync(sessionId, Endpoints.ActorOf(http), ct);
            var messages = session.Messages
                .Select(m => new
                {
                    role = m.Role,
                    text = string.Join("\n", m.Blocks.Where(b => b.Kind == BlockKind.Text).Select(b => StripContext(b.Text ?? ""))),
                    widgets = m.Blocks.Where(b => b.Widget is not null).Select(b => b.Widget).ToList(),
                })
                .Where(m => m.text.Length > 0 || m.widgets.Count > 0)
                .ToList();

            return Results.Ok(new { session.Id, session.TournamentId, messages });
        });

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

    /// <summary>Der Kontextblock gehört dem Modell, nicht dem Menschen.</summary>
    private static string StripContext(string text)
    {
        var end = text.IndexOf("</context>", StringComparison.Ordinal);
        return end < 0 ? text : text[(end + "</context>".Length)..].TrimStart();
    }
}
