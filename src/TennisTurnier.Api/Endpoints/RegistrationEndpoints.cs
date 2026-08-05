using Microsoft.AspNetCore.RateLimiting;
using TennisTurnier.Application.Registration;
using TennisTurnier.Application.Tournaments;

namespace TennisTurnier.Api.Endpoints;

/// <summary>
/// Die Selbstmeldung — von beiden Seiten.
///
/// Unter <c>/api</c> steht, was die Turnierleitung sieht: der Anmeldelink samt
/// Bedingungen und Zählstand, die Meldungen mit Kontaktdaten. Unter
/// <c>/public</c> steht der anonyme Weg, und der ist ausdrücklich karg — er
/// nennt nur, was auf einem Aushang stünde.
///
/// Der Token gehört in den Pfad und nicht in den Query-String: er landet dort
/// in Server- und Proxy-Protokollen mit ganzer Zeile, während der Pfad ohnehin
/// protokolliert wird. Gegen den Referer steht <c>Referrer-Policy</c>, gegen
/// ein durchgesickertes Token die Erneuerung.
/// </summary>
internal static class RegistrationEndpoints
{
    /// <summary>Die Ratenbegrenzung der anonymen Endpunkte, je Aufrufer-IP.</summary>
    public const string PublicPolicy = "oeffentliche-anmeldung";

    public static IEndpointRouteBuilder MapRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        MapManagement(app);
        MapPublic(app);

        return app;
    }

    private static void MapManagement(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tournaments/{tournamentId:guid}").WithTags("Anmeldung");

        group.MapGet("/registration", async (
            Guid tournamentId, ITournamentService service, CancellationToken ct) =>
            Results.Ok(await service.GetRegistrationAsync(tournamentId, ct)));

        group.MapPut("/registration", async (
            Guid tournamentId,
            ConfigureRegistrationRequest request,
            ITournamentService service,
            CancellationToken ct) =>
        {
            await service.ConfigureRegistrationAsync(tournamentId, request, ct);
            return Results.NoContent();
        });

        // Eigener Endpunkt und kein Feld im PUT: das alte Token wird damit
        // sofort wertlos, und jeder ausgehängte Zettel ist Makulatur. Das ist
        // eine Handlung mit Folgen, keine Attributänderung.
        group.MapPost("/registration/link/rotate", async (
            Guid tournamentId, ITournamentService service, CancellationToken ct) =>
        {
            await service.RotateRegistrationLinkAsync(tournamentId, ct);
            return Results.NoContent();
        });

        group.MapGet("/entries", async (
            Guid tournamentId, ITournamentService service, CancellationToken ct) =>
            Results.Ok(await service.ListEntriesAsync(tournamentId, ct))).WithTags("Meldungen");
    }

    private static void MapPublic(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/public/registrations")
            .WithTags("Öffentlich")
            .AllowAnonymous()
            .RequireRateLimiting(PublicPolicy);

        group.MapGet("/{token}", async (
            string token, IRegistrationService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(token, ct)));

        group.MapPost("/{token}", async (
            string token,
            SelfRegistrationRequest request,
            IRegistrationService service,
            CancellationToken ct) =>
            Results.Ok(await service.RegisterAsync(token, request, ct)));
    }
}
