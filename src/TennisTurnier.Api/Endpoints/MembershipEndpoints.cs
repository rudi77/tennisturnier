using TennisTurnier.Application.Membership;
using TennisTurnier.Application.Tournaments;

namespace TennisTurnier.Api.Endpoints;

/// <summary>
/// Der Beitritt — von beiden Seiten.
///
/// Unter <c>/api/tournaments/{id}</c> steht, was die Turnierleitung sieht: der
/// Beitrittslink samt Bedingungen und Zählstand, die Meldungen mit
/// Kontaktdaten. Unter <c>/api/join</c> steht der Weg dessen, der dem Link
/// folgt — angemeldet, aber noch ohne Rolle am Turnier, und deshalb mit einer
/// ausdrücklich kargen Auskunft: sie nennt nur, was auf einem Aushang stünde.
///
/// Angemeldet und trotzdem karg ist kein Widerspruch. Der Link ist die
/// Eintrittskarte, nicht der Ausweis: wer ihn hat, darf herein — er hat ihn
/// aber vielleicht nur weitergereicht bekommen, und bis zum Beitritt gehört er
/// nicht dazu.
///
/// Der Token gehört in den Pfad und nicht in den Query-String: er landet dort
/// in Server- und Proxy-Protokollen mit ganzer Zeile, während der Pfad ohnehin
/// protokolliert wird. Gegen den Referer steht <c>Referrer-Policy</c>, gegen
/// ein durchgesickertes Token die Erneuerung.
/// </summary>
internal static class MembershipEndpoints
{
    public static IEndpointRouteBuilder MapMembershipEndpoints(this IEndpointRouteBuilder app)
    {
        MapManagement(app);
        MapJoin(app);

        return app;
    }

    private static void MapManagement(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tournaments/{tournamentId:guid}").WithTags("Beitritt");

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

    /// <summary>
    /// Beitreten setzt ein Konto voraus — anonym gibt es hier nichts, auch
    /// keine Auskunft. Wer nicht angemeldet ist, bekommt 401 und wird von der
    /// Oberfläche zur Anmeldung geschickt; nach ihr steht er wieder hier.
    /// </summary>
    private static void MapJoin(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/join").WithTags("Beitritt").RequireAuthorization();

        group.MapGet("/{token}", async (
            string token, IJoinService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(token, ct)));

        group.MapPost("/{token}", async (
            string token,
            JoinRequest request,
            IJoinService service,
            CancellationToken ct) =>
            Results.Ok(await service.JoinAsync(token, request, ct)));
    }
}
