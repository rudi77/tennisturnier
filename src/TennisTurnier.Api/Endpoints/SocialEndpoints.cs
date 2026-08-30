using TennisTurnier.Application.Social;

namespace TennisTurnier.Api.Endpoints;

/// <summary>
/// Die Endpunkte, die aus MATCHDAY mehr machen als eine Turnierverwaltung:
/// Profile, Verbindungen und Verabredungen.
///
/// Alle verlangen ein Konto. Das ist keine zusätzliche Schranke, sondern die
/// Feststellung, dass es hier ohne eines nichts zu holen gäbe: die Sichtbarkeit
/// hängt an den Turnieren des Aufrufers (ADR-0013), und wer nicht angemeldet
/// ist, hat keine. Ein anonymer Aufruf bekäme auf jede dieser Adressen ein 404
/// — ein 401 sagt ihm wenigstens, woran es liegt.
/// </summary>
internal static class SocialEndpoints
{
    public static IEndpointRouteBuilder MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        MapProfiles(app);
        MapFeed(app);
        MapConnections(app);

        return app;
    }

    /// <summary>
    /// Mit wem der Aufrufer gespielt hat (ADR-0013).
    ///
    /// Nur die eigenen: der Kontaktgraph eines Fremden ist eine Aussage über
    /// dessen Mitspieler und nicht über ihn — wer ihn sehen will, sieht sein
    /// Profil an, und dort steht, was er mit dem Fragenden zu tun hat.
    /// </summary>
    private static void MapConnections(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/connections", async (
            IConnectionService service, CancellationToken ct) =>
            Results.Ok(await service.ListMineAsync(ct)))
            .WithTags("Kontakte")
            .RequireAuthorization();
    }

    /// <summary>
    /// Der Feed eines Turniers (ADR-0014).
    ///
    /// Unter dem Turnier, weil er ihm gehört — und weil damit derselbe
    /// Query-Filter greift wie für Draw, Spielplan und Ergebnisse. Ein Fremder
    /// bekommt hier 404 und nicht eine leere Liste: die leere Liste wäre die
    /// Aussage „dieses Turnier hat noch nichts geschrieben", und die steht ihm
    /// nicht zu.
    ///
    /// Kommentar und Löschen hängen dagegen am Eintrag und nicht am Turnier:
    /// die Id des Eintrags nennt sein Turnier bereits, und ein zweiter
    /// Bezeichner im Pfad wäre einer, den der Server gegen den ersten prüfen
    /// müsste.
    /// </summary>
    private static void MapFeed(IEndpointRouteBuilder app)
    {
        var feed = app.MapGroup("/api").WithTags("Feed").RequireAuthorization();

        feed.MapGet("/tournaments/{tournamentId:guid}/feed", async (
            Guid tournamentId,
            IFeedService service,
            CancellationToken ct,
            int limit = 50,
            DateTimeOffset? before = null) =>
            Results.Ok(await service.ListAsync(tournamentId, limit, before, ct)));

        feed.MapPost("/tournaments/{tournamentId:guid}/feed", async (
            Guid tournamentId,
            WritePostRequest request,
            IFeedService service,
            CancellationToken ct) =>
        {
            var post = await service.PostAsync(tournamentId, request, ct);
            return Results.Created($"/api/feed/{post.Id}", post);
        });

        feed.MapPost("/feed/{postId:guid}/comments", async (
            Guid postId,
            WritePostRequest request,
            IFeedService service,
            CancellationToken ct) =>
            Results.Ok(await service.CommentAsync(postId, request, ct)));

        feed.MapDelete("/feed/{postId:guid}", async (
            Guid postId, IFeedService service, CancellationToken ct) =>
        {
            await service.DeletePostAsync(postId, ct);
            return Results.NoContent();
        });

        feed.MapDelete("/feed/{postId:guid}/comments/{commentId:guid}", async (
            Guid postId, Guid commentId, IFeedService service, CancellationToken ct) =>
        {
            await service.DeleteCommentAsync(postId, commentId, ct);
            return Results.NoContent();
        });
    }

    private static void MapProfiles(IEndpointRouteBuilder app)
    {
        var profiles = app.MapGroup("/api").WithTags("Profil").RequireAuthorization();

        // Unter /players/{id}/profile und nicht unter /players/{id}: dort steht
        // seit ADR-0008 die Auskunft mit Kontaktdaten, und die hängt an einem
        // Turnier. Zwei Auskünfte über denselben Spieler mit verschiedenen
        // Regeln sollen auch verschieden heißen.
        profiles.MapGet("/players/{playerId:guid}/profile", async (
            Guid playerId, IPlayerProfileService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(playerId, ct)));

        // „Noch keiner" ist eine gültige Antwort und kein Fehler — wer
        // beigetreten ist, ohne je zu melden, hat keinen Spieler. Dieselbe
        // Form wie /api/me daneben.
        profiles.MapGet("/me/profile", async (IPlayerProfileService service, CancellationToken ct) =>
            await service.GetMineAsync(ct) is { } profile
                ? Results.Ok(profile)
                : Results.NoContent());

        // Antwortet mit dem fertigen Profil und nicht mit 204: dieser Aufruf
        // kann den Spieler anlegen, und der Aufrufer erfährt hier seine Id,
        // ohne ein zweites Mal zu fragen.
        profiles.MapPut("/me/profile", async (
            UpdateMyProfileRequest request,
            IPlayerProfileService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateMineAsync(request, ct)));
    }
}
