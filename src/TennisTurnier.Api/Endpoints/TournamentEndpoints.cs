using TennisTurnier.Application.Tournaments;

namespace TennisTurnier.Api.Endpoints;

internal static class TournamentEndpoints
{
    public static IEndpointRouteBuilder MapTournamentEndpoints(this IEndpointRouteBuilder app)
    {
        MapTournaments(app);
        MapFormatTemplates(app);
        MapPlayers(app);

        return app;
    }

    private static void MapTournaments(IEndpointRouteBuilder app)
    {
        var byClub = app.MapGroup("/api/clubs/{clubId:guid}/tournaments").WithTags("Turniere");

        byClub.MapGet("/", async (Guid clubId, ITournamentService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(clubId, ct)));

        byClub.MapPost("/", async (
            Guid clubId,
            CreateTournamentRequest request,
            ITournamentService service,
            CancellationToken ct) =>
        {
            var id = await service.CreateAsync(clubId, request, ct);
            return Results.Created($"/api/tournaments/{id}", new { id });
        });

        var tournaments = app.MapGroup("/api/tournaments/{tournamentId:guid}").WithTags("Turniere");

        tournaments.MapGet("/", async (Guid tournamentId, ITournamentService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(tournamentId, ct)));

        tournaments.MapPut("/", async (
            Guid tournamentId,
            UpdateTournamentRequest request,
            ITournamentService service,
            CancellationToken ct) =>
        {
            await service.UpdateAsync(tournamentId, request, ct);
            return Results.NoContent();
        });

        MapTransitions(tournaments);
        MapEntries(tournaments);
    }

    /// <summary>
    /// Die Zustandsübergänge sind eigene Endpunkte und kein Feld im PUT.
    ///
    /// „Auslosung zurücknehmen" verwirft den Draw und „Turniertag starten" ändert
    /// die Bedeutung jeder angezeigten Uhrzeit — beides sind Handlungen mit
    /// Folgen, keine Attributänderungen.
    /// </summary>
    private static void MapTransitions(RouteGroupBuilder tournaments)
    {
        var transitions = new (string Route, Func<ITournamentService, Guid, CancellationToken, Task> Action)[]
        {
            ("registration/open", (s, id, ct) => s.OpenRegistrationAsync(id, ct)),
            ("registration/close", (s, id, ct) => s.CloseRegistrationAsync(id, ct)),
            ("registration/reopen", (s, id, ct) => s.ReopenRegistrationAsync(id, ct)),
            ("draw", (s, id, ct) => s.GenerateDrawAsync(id, ct)),
            ("start", (s, id, ct) => s.StartAsync(id, ct)),
            ("complete", (s, id, ct) => s.CompleteAsync(id, ct)),
            ("abandon", (s, id, ct) => s.AbandonAsync(id, ct)),
            ("scheduling/match-day", (s, id, ct) => s.SwitchToMatchDayAsync(id, ct)),
            ("scheduling/planning", (s, id, ct) => s.SwitchToPlanningAsync(id, ct)),
        };

        foreach (var (route, action) in transitions)
        {
            tournaments.MapPost(route, async (
                Guid tournamentId,
                ITournamentService service,
                CancellationToken ct) =>
            {
                await action(service, tournamentId, ct);
                return Results.NoContent();
            });
        }
    }

    private static void MapEntries(RouteGroupBuilder tournaments)
    {
        var entries = tournaments.MapGroup("/entries").WithTags("Meldungen");

        entries.MapPost("/", async (
            Guid tournamentId,
            EnterTournamentRequest request,
            ITournamentService service,
            CancellationToken ct) =>
        {
            var id = await service.EnterAsync(tournamentId, request, ct);
            return Results.Created($"/api/tournaments/{tournamentId}/entries/{id}", new { id });
        });

        entries.MapPost("/{entryId:guid}/accept", async (
            Guid tournamentId, Guid entryId, ITournamentService service, CancellationToken ct) =>
        {
            await service.AcceptAsync(tournamentId, entryId, ct);
            return Results.NoContent();
        });

        entries.MapPost("/{entryId:guid}/waiting-list", async (
            Guid tournamentId, Guid entryId, ITournamentService service, CancellationToken ct) =>
        {
            await service.MoveToWaitingListAsync(tournamentId, entryId, ct);
            return Results.NoContent();
        });

        entries.MapPost("/{entryId:guid}/withdraw", async (
            Guid tournamentId, Guid entryId, ITournamentService service, CancellationToken ct) =>
        {
            await service.WithdrawAsync(tournamentId, entryId, ct);
            return Results.NoContent();
        });

        entries.MapPut("/{entryId:guid}/seed", async (
            Guid tournamentId,
            Guid entryId,
            SetSeedRequest request,
            ITournamentService service,
            CancellationToken ct) =>
        {
            await service.SetSeedAsync(tournamentId, entryId, request, ct);
            return Results.NoContent();
        });
    }

    private static void MapFormatTemplates(IEndpointRouteBuilder app)
    {
        var byClub = app.MapGroup("/api/clubs/{clubId:guid}/format-templates").WithTags("Formatvorlagen");

        byClub.MapGet("/", async (Guid clubId, IFormatTemplateService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(clubId, ct)));

        byClub.MapPost("/", async (
            Guid clubId,
            SaveFormatTemplateRequest request,
            IFormatTemplateService service,
            CancellationToken ct) =>
        {
            var id = await service.CreateAsync(clubId, request, ct);
            return Results.Created($"/api/format-templates/{id}", new { id });
        });

        byClub.MapPost("/{templateId:guid}/copy", async (
            Guid clubId,
            Guid templateId,
            CopyFormatTemplateRequest request,
            IFormatTemplateService service,
            CancellationToken ct) =>
        {
            var id = await service.CopyAsync(clubId, templateId, request, ct);
            return Results.Created($"/api/format-templates/{id}", new { id });
        });

        var templates = app.MapGroup("/api/format-templates/{templateId:guid}").WithTags("Formatvorlagen");

        templates.MapGet("/", async (Guid templateId, IFormatTemplateService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(templateId, ct)));

        templates.MapPut("/", async (
            Guid templateId,
            SaveFormatTemplateRequest request,
            IFormatTemplateService service,
            CancellationToken ct) =>
        {
            await service.UpdateAsync(templateId, request, ct);
            return Results.NoContent();
        });
    }

    private static void MapPlayers(IEndpointRouteBuilder app)
    {
        var players = app.MapGroup("/api/players").WithTags("Spieler");

        players.MapGet("/", async (
            string q,
            IPlayerService service,
            CancellationToken ct,
            int limit = 20) =>
            Results.Ok(await service.SearchAsync(q, limit, ct)));

        players.MapPost("/", async (CreatePlayerRequest request, IPlayerService service, CancellationToken ct) =>
        {
            var id = await service.CreatePlayerAsync(request, ct);
            return Results.Created($"/api/players/{id}", new { id });
        });

        // Kontaktdaten hängen an einem Verein, weil Spieler selbst keinem
        // gehören (ADR-0008) und der Query-Filter hier nicht greift.
        players.MapGet("/{playerId:guid}", async (
            Guid playerId,
            Guid clubId,
            IPlayerService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetAsync(clubId, playerId, ct)));

        app.MapPost("/api/participants", async (
            CreateParticipantRequest request,
            IPlayerService service,
            CancellationToken ct) =>
        {
            var participant = await service.CreateParticipantAsync(request, ct);
            return Results.Created($"/api/participants/{participant.Id}", participant);
        }).WithTags("Spieler");
    }
}
