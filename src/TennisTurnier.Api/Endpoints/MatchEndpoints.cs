using TennisTurnier.Application.Tournaments;

namespace TennisTurnier.Api.Endpoints;

internal static class MatchEndpoints
{
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var tournaments = app.MapGroup("/api/tournaments/{tournamentId:guid}").WithTags("Bracket");

        tournaments.MapGet("/phases", async (
            Guid tournamentId,
            IMatchService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetPhasesAsync(tournamentId, ct)));

        tournaments.MapGet("/phases/{phaseId:guid}/standings", async (
            Guid tournamentId,
            Guid phaseId,
            IMatchService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetStandingsAsync(tournamentId, phaseId, ct)));

        // Der Spielplan (ADR-0002): rechnen und übernehmen sind zwei Schritte.
        // Ein Solverlauf, der den Plan still überschreibt, ist genau das, was
        // Turnierleitungen dazu bringt, die Automatik abzuschalten.
        var schedule = tournaments.MapGroup("/schedule").WithTags("Spielplan");

        schedule.MapPost("/proposal", async (
            Guid tournamentId,
            ISchedulingService service,
            CancellationToken ct) =>
            Results.Ok(await service.ProposeAsync(tournamentId, ct)));

        schedule.MapPost("/confirm", async (
            Guid tournamentId,
            ConfirmScheduleRequest request,
            ISchedulingService service,
            CancellationToken ct) =>
            Results.Ok(await service.ConfirmAsync(tournamentId, request, ct)));

        // Der Turniertag (ADR-0002): hier zählt die Reihenfolge auf dem Platz,
        // nicht das Zeitraster. Eine feste Startzeit wäre eine Behauptung, die
        // beim ersten langen Match zerfällt.
        tournaments.MapGet("/courts", async (
            Guid tournamentId,
            ICourtQueueService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetBoardAsync(tournamentId, ct)))
            .WithTags("Turniertag");

        tournaments.MapPost("/courts/{courtId:guid}/queue", async (
            Guid tournamentId,
            Guid courtId,
            ReorderQueueRequest request,
            ICourtQueueService service,
            CancellationToken ct) =>
        {
            await service.ReorderAsync(tournamentId, courtId, request, ct);
            return Results.NoContent();
        }).WithTags("Turniertag");

        var assignments = app.MapGroup("/api/assignments/{assignmentId:guid}").WithTags("Turniertag");

        assignments.MapPost("/call", async (
            Guid assignmentId,
            ICourtQueueService service,
            CancellationToken ct) =>
        {
            await service.CallAsync(assignmentId, ct);
            return Results.NoContent();
        });

        assignments.MapPost("/start", async (
            Guid assignmentId,
            ICourtQueueService service,
            CancellationToken ct) =>
        {
            await service.StartAsync(assignmentId, ct);
            return Results.NoContent();
        });

        assignments.MapPost("/finish", async (
            Guid assignmentId,
            ICourtQueueService service,
            CancellationToken ct) =>
        {
            await service.FinishAsync(assignmentId, ct);
            return Results.NoContent();
        });

        assignments.MapPost("/suspend", async (
            Guid assignmentId,
            ICourtQueueService service,
            CancellationToken ct) =>
        {
            await service.SuspendAsync(assignmentId, ct);
            return Results.NoContent();
        });

        assignments.MapPost("/resume", async (
            Guid assignmentId,
            ResumeMatchRequest request,
            ICourtQueueService service,
            CancellationToken ct) =>
            Results.Ok(new { id = await service.ResumeAsync(assignmentId, request, ct) }));

        assignments.MapPost("/promise", async (
            Guid assignmentId,
            PromiseStartRequest request,
            ICourtQueueService service,
            CancellationToken ct) =>
        {
            await service.PromiseAsync(assignmentId, request, ct);
            return Results.NoContent();
        });

        var matches = app.MapGroup("/api/matches/{matchId:guid}").WithTags("Ergebnisse");

        matches.MapPut("/result", async (
            Guid matchId,
            RecordResultRequest request,
            IMatchService service,
            CancellationToken ct) =>
        {
            await service.RecordResultAsync(matchId, request, ct);
            return Results.NoContent();
        });

        // Eigener Endpunkt statt eines leeren PUT: eine Korrektur ist eine
        // Handlung mit Folgen — sie kann an einem bereits gespielten Folgematch
        // scheitern.
        matches.MapDelete("/result", async (Guid matchId, IMatchService service, CancellationToken ct) =>
        {
            await service.ClearResultAsync(matchId, ct);
            return Results.NoContent();
        });

        matches.MapPost("/court", async (
            Guid matchId,
            AssignCourtRequest request,
            IMatchService service,
            CancellationToken ct) =>
        {
            // Antwortet mit 200 und nicht 201, weil die Antwort mehr trägt als
            // die neue Id: die Verstöße gegen harte Constraints, die diese
            // Zuweisung erzeugt. Sie blockieren nicht, sollen aber sichtbar
            // sein (ADR-0002).
            var result = await service.AssignCourtAsync(matchId, request, ct);
            return Results.Ok(result);
        });

        app.MapDelete("/api/court-assignments/{assignmentId:guid}", async (
            Guid assignmentId,
            IMatchService service,
            CancellationToken ct) =>
        {
            await service.RemoveAssignmentAsync(assignmentId, ct);
            return Results.NoContent();
        }).WithTags("Ergebnisse");

        return app;
    }
}
