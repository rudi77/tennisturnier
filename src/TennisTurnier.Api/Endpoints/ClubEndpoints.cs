using TennisTurnier.Application.Clubs;

namespace TennisTurnier.Api.Endpoints;

/// <summary>
/// Die Endpunkte kennen nur <see cref="IClubService"/> und die Verträge aus der
/// Anwendungsschicht — keine Domänenentitäten, keinen DbContext (ADR-0005).
///
/// Sie enthalten bewusst auch keine Berechtigungsprüfung: die liegt im
/// Anwendungsfall, damit sie nicht davon abhängt, über welchen Endpunkt jemand
/// hereinkommt.
/// </summary>
internal static class ClubEndpoints
{
    public static IEndpointRouteBuilder MapClubEndpoints(this IEndpointRouteBuilder app)
    {
        var clubs = app.MapGroup("/api/clubs").WithTags("Vereine");

        clubs.MapGet("/", async (IClubService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct)));

        clubs.MapPost("/", async (CreateClubRequest request, IClubService service, CancellationToken ct) =>
        {
            var id = await service.CreateAsync(request, ct);
            return Results.Created($"/api/clubs/{id}", new { id });
        });

        clubs.MapGet("/{clubId:guid}", async (Guid clubId, IClubService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(clubId, ct)));

        clubs.MapPut("/{clubId:guid}", async (
            Guid clubId,
            UpdateClubRequest request,
            IClubService service,
            CancellationToken ct) =>
        {
            await service.UpdateAsync(clubId, request, ct);
            return Results.NoContent();
        });

        MapCourtEndpoints(clubs);

        return app;
    }

    private static void MapCourtEndpoints(RouteGroupBuilder clubs)
    {
        var courts = clubs.MapGroup("/{clubId:guid}/courts").WithTags("Plätze");

        courts.MapGet("/", async (Guid clubId, IClubService service, CancellationToken ct) =>
            Results.Ok((await service.GetAsync(clubId, ct)).Courts));

        courts.MapPost("/", async (
            Guid clubId,
            CreateCourtRequest request,
            IClubService service,
            CancellationToken ct) =>
        {
            var id = await service.AddCourtAsync(clubId, request, ct);
            return Results.Created($"/api/clubs/{clubId}/courts/{id}", new { id });
        });

        courts.MapPut("/{courtId:guid}", async (
            Guid clubId,
            Guid courtId,
            UpdateCourtRequest request,
            IClubService service,
            CancellationToken ct) =>
        {
            await service.UpdateCourtAsync(clubId, courtId, request, ct);
            return Results.NoContent();
        });

        courts.MapPost("/{courtId:guid}/availability", async (
            Guid clubId,
            Guid courtId,
            CreateAvailabilityRequest request,
            IClubService service,
            CancellationToken ct) =>
        {
            var id = await service.AddAvailabilityAsync(clubId, courtId, request, ct);
            return Results.Created($"/api/clubs/{clubId}/courts/{courtId}/availability/{id}", new { id });
        });

        courts.MapDelete("/{courtId:guid}/availability/{windowId:guid}", async (
            Guid clubId,
            Guid courtId,
            Guid windowId,
            IClubService service,
            CancellationToken ct) =>
        {
            await service.RemoveAvailabilityAsync(clubId, courtId, windowId, ct);
            return Results.NoContent();
        });

        courts.MapPost("/{courtId:guid}/blocks", async (
            Guid clubId,
            Guid courtId,
            CreateBlockRequest request,
            IClubService service,
            CancellationToken ct) =>
        {
            var id = await service.AddBlockAsync(clubId, courtId, request, ct);
            return Results.Created($"/api/clubs/{clubId}/courts/{courtId}/blocks/{id}", new { id });
        });

        courts.MapDelete("/{courtId:guid}/blocks/{blockId:guid}", async (
            Guid clubId,
            Guid courtId,
            Guid blockId,
            IClubService service,
            CancellationToken ct) =>
        {
            await service.RemoveBlockAsync(clubId, courtId, blockId, ct);
            return Results.NoContent();
        });

        courts.MapGet("/{courtId:guid}/free-windows", async (
            Guid clubId,
            Guid courtId,
            DateTimeOffset from,
            DateTimeOffset to,
            IClubService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetFreeWindowsAsync(clubId, courtId, from, to, ct)));
    }
}
