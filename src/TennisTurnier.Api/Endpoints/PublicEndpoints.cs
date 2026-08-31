using Microsoft.AspNetCore.Authorization;
using Microsoft.Net.Http.Headers;
using TennisTurnier.Application.PublicView;

namespace TennisTurnier.Api.Endpoints;

/// <summary>
/// Die öffentliche Ansicht (ADR-0003).
///
/// Ohne Anmeldung, mit ETag und einer kurzen Cache-Dauer. Die Antwort kommt
/// vorserialisiert aus der Projektion und wird hier nicht mehr angefasst — jede
/// Anreicherung an dieser Stelle wäre eine zweite Stelle, an der Daten
/// öffentlich werden können, und damit eine, die niemand prüft.
/// </summary>
internal static class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/public/tournaments").WithTags("Öffentlich").AllowAnonymous();

        group.MapGet("/{tournamentId:guid}", async (
            Guid tournamentId,
            HttpContext http,
            IPublicViewService service,
            CancellationToken ct) =>
        {
            var lookup = await service.GetAsync(tournamentId, ct);

            if (!lookup.Visible)
            {
                return Results.NotFound();
            }

            // Sichtbar, aber noch nichts zu zeigen: die Projektion entsteht mit
            // der Auslosung. Ein 404 wäre hier gelogen — der Zuschauer läse
            // „gibt es nicht oder ist privat" über ein Turnier, das offen ist
            // und nur noch nicht ausgelost.
            if (lookup.Snapshot is not { } snapshot)
            {
                return Results.NoContent();
            }

            var headers = http.Response.GetTypedHeaders();
            headers.ETag = new EntityTagHeaderValue(snapshot.ETag);
            // Hier stand einmal eine Vorratshaltung von 15 Sekunden. Seit ein
            // Turnier privat sein kann, wäre sie ein Leck: wer zusieht, während
            // die Turnierleitung wieder zumacht, sähe weiter zu — und ein
            // gemeinsamer Zwischenspeicher zeigte die Ansicht sogar jemandem,
            // der sie nie geladen hat. `no-cache` heißt nicht „nicht
            // speichern", sondern „jedes Mal nachfragen": das Ersparnis über
            // den ETag bleibt, die Entscheidung über die Sichtbarkeit fällt
            // aber wieder bei jedem Abruf.
            headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            headers.LastModified = snapshot.UpdatedAt;

            // Ein 304 spart bei einem Bracket mit 64 Matches den ganzen Body —
            // und am Turniertag hängen viele Clients an derselben Ansicht.
            return Matches(http.Request, snapshot.ETag)
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Text(snapshot.Json, "application/json", System.Text.Encoding.UTF8);
        });

        // Der Neuaufbau auf Anweisung — die einzige schreibende Handlung an der
        // Ansicht, und ausdrücklich nicht öffentlich. Die Berechtigung prüft der
        // Anwendungsfall, damit ein fremdes Turnier als 404 endet (ADR-0004).
        app.MapPost("/api/tournaments/{tournamentId:guid}/public-view/rebuild", async (
            Guid tournamentId,
            IPublicViewService service,
            CancellationToken ct) =>
        {
            await service.RebuildOnDemandAsync(tournamentId, ct);
            return Results.NoContent();
        }).WithTags("Öffentlich");

        return app;
    }

    /// <summary>
    /// Prüft <c>If-None-Match</c>. Der Stern trifft jede vorhandene Ressource;
    /// sonst genügt ein Treffer aus der Liste. Ein schwacher Vergleich reicht
    /// hier, weil der ETag ohnehin aus dem Inhalt gebildet wird.
    /// </summary>
    private static bool Matches(HttpRequest request, string etag)
    {
        var candidates = request.GetTypedHeaders().IfNoneMatch;

        return candidates.Count > 0
            && candidates.Any(candidate =>
                candidate.Equals(EntityTagHeaderValue.Any)
                || string.Equals(candidate.Tag.Value, etag, StringComparison.Ordinal));
    }
}
