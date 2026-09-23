using System.Security.Claims;
using System.Text.Json;
using Matchday.Domain;
using Matchday.Server.Auth;
using Microsoft.Extensions.Options;
using Matchday.Server.Live;
using Matchday.Server.Storage;

namespace Matchday.Server.Api;

/// <summary>
/// Die HTTP-API, die die Widgets direkt rufen — am Modell vorbei (ADR-0016).
/// Wer handelt, steht in Kopfzeilen: der Browser in <c>X-Matchday-Client</c>,
/// der Verwalterlink in <c>X-Admin-Token</c>, der Eintragen-Link in
/// <c>X-Scorer-Token</c>.
/// </summary>
public static class Endpoints
{
    public const string ClientHeader = "X-Matchday-Client";
    public const string AdminHeader = "X-Admin-Token";
    public const string ScorerHeader = "X-Scorer-Token";

    public static void MapMatchday(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        var tournaments = api.MapGroup("/tournaments");

        tournaments.MapPost("/", async (HttpContext http, TournamentActions actions, CreateTournamentRequest request, CancellationToken ct) =>
        {
            var t = await actions.CreateAsync(ActorOf(http), request, ct);
            return Results.Created($"/api/tournaments/{t.Id}", Admin(t, http));
        });

        tournaments.MapGet("/", async (HttpContext http, TournamentActions actions, CancellationToken ct) =>
        {
            var mine = await actions.ListMineAsync(ActorOf(http), ct);
            return Results.Ok(mine.Select(ViewBuilder.Summarize));
        });

        // Der Verwalterlink ist ein Schlüssel, kein Besitz — er geht durch
        // RequireLogin und nicht durch ActorOf. Steht die Anmeldung, muss man
        // angemeldet sein, um ihn einzulösen; was man damit darf, entscheidet
        // weiterhin das Token.
        tournaments.MapGet("/by-admin/{token}", async (HttpContext http, TournamentActions actions, string token, CancellationToken ct) =>
        {
            RequireLogin(http);
            return Results.Ok(Admin(await actions.GetByAdminTokenAsync(token, ct), http));
        });

        // Der Eintragen-Link: wie der Verwalterlink ein Schlüssel, nur kleiner.
        // Zurück kommt die Sicht und sein eigenes Token, nie das der Verwaltung.
        tournaments.MapGet("/by-scorer/{token}", async (HttpContext http, TournamentActions actions, string token, CancellationToken ct) =>
        {
            RequireLogin(http);
            var t = await actions.GetByScorerTokenAsync(token, ct);
            return Results.Ok(new ScorerAccess(ViewBuilder.Build(t), t.ScorerToken));
        });

        tournaments.MapGet("/{id:guid}", async (TournamentActions actions, Guid id, CancellationToken ct) =>
            Results.Ok(ViewBuilder.Build(await actions.GetAsync(id, ct))));

        tournaments.MapGet("/{id:guid}/live", Live);

        tournaments.MapPut("/{id:guid}", async (HttpContext http, TournamentActions actions, Guid id, UpdateTournamentRequest request, CancellationToken ct) =>
            Results.Ok(Admin(await actions.UpdateAsync(ActorOf(http), id, request, ct), http)));

        tournaments.MapDelete("/{id:guid}", async (HttpContext http, TournamentActions actions, Guid id, CancellationToken ct) =>
        {
            await actions.DeleteAsync(ActorOf(http), id, ct);
            return Results.NoContent();
        });

        tournaments.MapPost("/{id:guid}/participants", async (HttpContext http, TournamentActions actions, Guid id, AddParticipantsRequest request, CancellationToken ct) =>
            Results.Ok(Admin(await actions.AddParticipantsAsync(ActorOf(http), id, request.Names, ct), http)));

        tournaments.MapPost("/{id:guid}/participants/random-teams", async (HttpContext http, TournamentActions actions, Guid id, RandomTeamsRequest request, CancellationToken ct) =>
            Results.Ok(Admin(await actions.AddRandomTeamsAsync(ActorOf(http), id, request.Players, ct), http)));

        tournaments.MapDelete("/{id:guid}/participants/{participantId:guid}", async (HttpContext http, TournamentActions actions, Guid id, Guid participantId, CancellationToken ct) =>
            Results.Ok(Admin(await actions.RemoveParticipantAsync(ActorOf(http), id, participantId, ct), http)));

        tournaments.MapPost("/{id:guid}/draw", async (HttpContext http, TournamentActions actions, Guid id, CancellationToken ct) =>
            Results.Ok(Admin(await actions.DrawAsync(ActorOf(http), id, ct), http)));

        tournaments.MapDelete("/{id:guid}/draw", async (HttpContext http, TournamentActions actions, Guid id, CancellationToken ct) =>
            Results.Ok(Admin(await actions.UndoDrawAsync(ActorOf(http), id, ct), http)));

        tournaments.MapPut("/{id:guid}/matches/{matchId:guid}/result", async (HttpContext http, TournamentActions actions, Guid id, Guid matchId, ResultRequest request, CancellationToken ct) =>
            Scored(await actions.RecordResultAsync(ActorOf(http), id, matchId, request, ct), http));

        tournaments.MapDelete("/{id:guid}/matches/{matchId:guid}/result", async (HttpContext http, TournamentActions actions, Guid id, Guid matchId, CancellationToken ct) =>
            Scored(await actions.ClearResultAsync(ActorOf(http), id, matchId, ct), http));

        tournaments.MapPost("/{id:guid}/matches/{matchId:guid}/live", async (HttpContext http, TournamentActions actions, Guid id, Guid matchId, LiveRequest request, CancellationToken ct) =>
            Scored(await actions.LiveAsync(ActorOf(http), id, matchId, request, ct), http));

        tournaments.MapPost("/{id:guid}/admin-token/rotate", async (HttpContext http, TournamentActions actions, Guid id, CancellationToken ct) =>
            Results.Ok(Admin(await actions.RotateAdminTokenAsync(ActorOf(http), id, ct), http)));
    }

    /// <summary>Server-Sent Events: jede Änderung als vollständige Sicht, dazwischen ein Lebenszeichen.</summary>
    internal static async Task Live(HttpContext http, TournamentActions actions, LiveHub hub, Guid id, CancellationToken ct)
    {
        var tournament = await actions.GetAsync(id, ct);

        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";

        using var subscription = hub.Subscribe(id, out var reader);
        await Write(http, "view", ViewBuilder.Build(tournament), ct);

        // Kein Abbruch in der Bedingung: jeder Ausgang dieser Schleife ist ein
        // return, und ein abgebrochener Aufruf kommt als OperationCanceledException.
        while (true)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(hub.KeepAlive);

            try
            {
                var view = await reader.ReadAsync(timeout.Token);

                if (view is null)
                {
                    await Write(http, "deleted", new { id }, ct);
                    return;
                }

                await Write(http, "view", view, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await http.Response.WriteAsync(": keepalive\n\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal static async Task Write(HttpContext http, string eventName, object payload, CancellationToken ct)
    {
        await http.Response.WriteAsync($"event: {eventName}\ndata: {JsonSerializer.Serialize(payload, TournamentStore.Json)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    /// <summary>
    /// Wer handelt. Die eine Stelle, durch die jeder besitzergebundene Aufruf
    /// läuft — und damit die eine Stelle, an der die Anmeldung hängt.
    ///
    /// Ohne Anmeldung bleibt es beim Browser, der sich selbst benennt
    /// (ADR-0016). Mit Anmeldung zählt das Konto: Die Kennung kommt aus dem
    /// geprüften Token und nicht mehr aus einer Kopfzeile, die jeder setzen
    /// kann. Damit folgen einem die eigenen Turniere auch auf ein anderes
    /// Gerät — und ein fremder Browser kommt nicht mehr an sie heran, indem er
    /// eine Kennung errät (ADR-0019).
    /// </summary>
    internal static Actor ActorOf(HttpContext http)
    {
        var admin = http.Request.Headers[AdminHeader].FirstOrDefault();
        var scorer = http.Request.Headers[ScorerHeader].FirstOrDefault();

        if (Required(http))
        {
            return new Actor(AccountOf(http), admin, scorer);
        }

        var client = http.Request.Headers[ClientHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(client) || client.Length > 100)
        {
            throw new ForbiddenException($"Die Kopfzeile {ClientHeader} fehlt.");
        }

        return new Actor(client, admin, scorer);
    }

    /// <summary>
    /// Nur die Anmeldung prüfen, ohne einen Handelnden zu brauchen — für
    /// Endpunkte, die keinem Eigentümer gehören und trotzdem nicht offen
    /// stehen sollen, wenn der Schalter an ist.
    /// </summary>
    internal static void RequireLogin(HttpContext http)
    {
        if (Required(http))
        {
            _ = AccountOf(http);
        }
    }

    private static bool Required(HttpContext http) =>
        http.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.Required;

    /// <summary>
    /// Die Kennung des angemeldeten Kontos. Das Präfix hält sie von den
    /// selbst vergebenen Browserkennungen getrennt: Sonst könnte ein Browser,
    /// der sich eine Google-Id als Kennung gibt, nach dem Abschalten der
    /// Anmeldung fremde Turniere sehen.
    /// </summary>
    private static string AccountOf(HttpContext http)
    {
        var subject = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? http.User.FindFirst("sub")?.Value;

        if (http.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(subject))
        {
            throw new UnauthorizedException("Dafür musst du angemeldet sein.");
        }

        return $"google:{subject}";
    }

    internal static string BaseUrl(HttpContext http)
    {
        var forwardedProto = http.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        var forwardedHost = http.Request.Headers["X-Forwarded-Host"].FirstOrDefault();
        var scheme = string.IsNullOrEmpty(forwardedProto) ? http.Request.Scheme : forwardedProto;
        var host = string.IsNullOrEmpty(forwardedHost) ? http.Request.Host.Value : forwardedHost;
        return $"{scheme}://{host}";
    }

    /// <summary>Was die Verwaltung bekommt: die Sicht plus die beiden Links.</summary>
    private static object Admin(Tournament t, HttpContext http) => new AdminView(
        ViewBuilder.Build(t), ViewBuilder.Links(t, BaseUrl(http)), t.AdminToken);

    /// <summary>
    /// Die Antwort auf ein Eintragen. Die Verwaltung bekommt wie sonst ihre
    /// Sicht samt Links; wer nur den Eintragen-Link hat, bekommt die Sicht —
    /// und damit nie das Verwaltertoken zu sehen.
    /// </summary>
    private static IResult Scored(Tournament t, HttpContext http) =>
        Results.Ok(ActorOf(http).MayManage(t) ? Admin(t, http) : new ScorerAccess(ViewBuilder.Build(t), t.ScorerToken));
}

public sealed record AdminView(TournamentView Tournament, TournamentLinks Links, string AdminToken);
