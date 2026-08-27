using TennisTurnier.Application.Security;

namespace TennisTurnier.Api.Endpoints;

/// <summary>
/// Rollen an einem Turnier: Schiedsrichter und weitere Turnierleiter berufen und
/// entziehen.
///
/// Der Endpunkt, der lange fehlte. Rollen vergibt, wer eine Rolle hat — und
/// nach einer frischen Migration hatte niemand eine. Für die erste sorgen
/// inzwischen die beiden Bootstraps; von hier an geht es über diese Routen.
/// </summary>
internal static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tournaments/{tournamentId:guid}/roles").WithTags("Rollen");

        group.MapGet("/", async (Guid tournamentId, IRoleService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(tournamentId, ct)));

        group.MapPost("/", async (
            Guid tournamentId,
            GrantRoleRequest request,
            IRoleService service,
            CancellationToken ct) =>
        {
            var ergebnis = await service.GrantAsync(tournamentId, request, ct);

            // Auch die Einladung ist ein Created: es ist etwas entstanden, das
            // sich unter derselben Adresse zurücknehmen lässt. Was es ist,
            // sagt „invited" — die Oberfläche meldet danach entweder „berufen"
            // oder „eingeladen, wird beim ersten Login Mitglied".
            return Results.Created(
                $"/api/tournaments/{tournamentId}/roles/{ergebnis.Id}",
                new { id = ergebnis.Id, invited = ergebnis.Invited });
        });

        group.MapDelete("/{assignmentId:guid}", async (
            Guid tournamentId, Guid assignmentId, IRoleService service, CancellationToken ct) =>
        {
            await service.RevokeAsync(tournamentId, assignmentId, ct);
            return Results.NoContent();
        });

        return app;
    }
}
