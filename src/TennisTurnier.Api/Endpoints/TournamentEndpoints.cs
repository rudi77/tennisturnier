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
        var all = app.MapGroup("/api/tournaments").WithTags("Turniere");

        // Der Einstieg: die Turniere des Aufrufers. Welche das sind, entscheidet
        // der Query-Filter — hier steht keine zweite Bedingung.
        all.MapGet("/", async (ITournamentService service, CancellationToken ct) =>
            Results.Ok(await service.ListMineAsync(ct)));

        all.MapPost("/", async (
            CreateTournamentRequest request,
            ITournamentService service,
            CancellationToken ct) =>
        {
            var id = await service.CreateAsync(request, ct);
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

        // Eigener Endpunkt und kein Feld im PUT darüber: hier bedeutet ein
        // leeres Satzformat „zurück zur Vorlage", und im gemeinsamen Aufruf
        // wäre dieses Leer nicht von „nicht mitgeschickt" zu unterscheiden.
        tournaments.MapPut("/match-format", async (
            Guid tournamentId,
            SetMatchFormatRequest request,
            ITournamentService service,
            CancellationToken ct) =>
        {
            await service.SetMatchFormatAsync(tournamentId, request, ct);
            return Results.NoContent();
        });

        // Eigener Verb-Endpunkt und kein Zustandsübergang: Löschen ist keine
        // Station im Lebenszyklus, sondern sein Ende ohne Spur. Der Abbruch
        // daneben ist das Gegenteil — er beendet und lässt lesbar.
        tournaments.MapDelete("/", async (
            Guid tournamentId, ITournamentService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(tournamentId, ct);
            return Results.NoContent();
        });

        MapTransitions(tournaments);
        MapEntries(tournaments);
        MapTeams(tournaments);
        MapCourts(tournaments);
    }

    /// <summary>
    /// Die Plätze des Turniers samt ihren Zeiten.
    ///
    /// Sie hingen einmal am Verein. Dass sie jetzt am Turnier hängen, ist der
    /// Kern des Umbaus: reserviert wird außerhalb dieser Anwendung, und was
    /// zugesagt ist, gilt für dieses Turnier und für kein anderes.
    /// </summary>
    private static void MapCourts(RouteGroupBuilder tournaments)
    {
        var courts = tournaments.MapGroup("/courts").WithTags("Plätze");

        courts.MapPost("/", async (
            Guid tournamentId,
            CreateCourtRequest request,
            ITournamentService service,
            CancellationToken ct) =>
        {
            var id = await service.AddCourtAsync(tournamentId, request, ct);
            return Results.Created($"/api/tournaments/{tournamentId}/courts/{id}", new { id });
        });

        courts.MapPut("/{courtId:guid}", async (
            Guid tournamentId,
            Guid courtId,
            UpdateCourtRequest request,
            ITournamentService service,
            CancellationToken ct) =>
        {
            await service.UpdateCourtAsync(tournamentId, courtId, request, ct);
            return Results.NoContent();
        });

        courts.MapDelete("/{courtId:guid}", async (
            Guid tournamentId, Guid courtId, ITournamentService service, CancellationToken ct) =>
        {
            await service.RemoveCourtAsync(tournamentId, courtId, ct);
            return Results.NoContent();
        });

        // Die Massenanlage steht vor der Einzelanlage, weil sie der Normalfall
        // ist: „beide Plätze, beide Turniertage, acht bis zweiundzwanzig".
        courts.MapPost("/windows", async (
            Guid tournamentId,
            CreateCourtWindowsRequest request,
            ITournamentService service,
            CancellationToken ct) =>
            Results.Ok(new { created = await service.AddCourtWindowsAsync(tournamentId, request, ct) }));

        courts.MapPost("/{courtId:guid}/windows", async (
            Guid tournamentId,
            Guid courtId,
            CreateCourtWindowRequest request,
            ITournamentService service,
            CancellationToken ct) =>
        {
            var id = await service.AddCourtWindowAsync(tournamentId, courtId, request, ct);
            return Results.Created($"/api/tournaments/{tournamentId}/courts/{courtId}/windows/{id}", new { id });
        });

        courts.MapDelete("/{courtId:guid}/windows/{windowId:guid}", async (
            Guid tournamentId,
            Guid courtId,
            Guid windowId,
            ITournamentService service,
            CancellationToken ct) =>
        {
            await service.RemoveCourtWindowAsync(tournamentId, courtId, windowId, ct);
            return Results.NoContent();
        });
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

        // Der zweite Weg ins Feld, neben dem Anmeldelink. Eigener Endpunkt und
        // kein Sonderfall von POST /entries: er nimmt eine ganze Liste entgegen
        // und antwortet mit einem Bericht statt mit einer Id.
        entries.MapPost("/import", async (
            Guid tournamentId,
            ImportEntriesRequest request,
            IEntryImportService service,
            CancellationToken ct) =>
            Results.Ok(await service.ImportAsync(tournamentId, request, ct)));

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

    /// <summary>
    /// Die Teams eines Doppels, dessen Paare die Turnierleitung bildet.
    ///
    /// Eigene Gruppe neben den Meldungen: ein Team ist zwar eine Meldung, aber
    /// es entsteht nicht durch Melden. Wer es unter <c>/entries</c> anlegte,
    /// müsste dort zwei Sorten POST unterscheiden — eine für Menschen, eine
    /// für Paare.
    /// </summary>
    private static void MapTeams(RouteGroupBuilder tournaments)
    {
        var teams = tournaments.MapGroup("/teams").WithTags("Teams");

        teams.MapPost("/", async (
            Guid tournamentId,
            FormTeamRequest request,
            ITeamFormationService service,
            CancellationToken ct) =>
        {
            var id = await service.FormAsync(tournamentId, request, ct);
            return Results.Created($"/api/tournaments/{tournamentId}/teams/{id}", new { id });
        });

        // Das Los. Eigener Endpunkt und kein Schalter am Anlegen: es betrifft
        // alle offenen Meldungen auf einmal und antwortet mit einem Bericht.
        teams.MapPost("/draw", async (
            Guid tournamentId, ITeamFormationService service, CancellationToken ct) =>
            Results.Ok(await service.DrawAsync(tournamentId, ct)));

        teams.MapDelete("/{teamEntryId:guid}", async (
            Guid tournamentId, Guid teamEntryId, ITeamFormationService service, CancellationToken ct) =>
        {
            await service.DisbandAsync(tournamentId, teamEntryId, ct);
            return Results.NoContent();
        });
    }

    private static void MapFormatTemplates(IEndpointRouteBuilder app)
    {
        var all = app.MapGroup("/api/format-templates").WithTags("Formatvorlagen");

        all.MapGet("/", async (IFormatTemplateService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct)));

        all.MapPost("/", async (
            SaveFormatTemplateRequest request,
            IFormatTemplateService service,
            CancellationToken ct) =>
        {
            var id = await service.CreateAsync(request, ct);
            return Results.Created($"/api/format-templates/{id}", new { id });
        });

        all.MapPost("/{templateId:guid}/copy", async (
            Guid templateId,
            CopyFormatTemplateRequest request,
            IFormatTemplateService service,
            CancellationToken ct) =>
        {
            var id = await service.CopyAsync(templateId, request, ct);
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

        // Kontaktdaten hängen am Turnier, weil Spieler selbst keinem gehören
        // (ADR-0008) und der Query-Filter hier nicht greift. Das Turnier steht
        // deshalb im Pfad und nicht im Query-String: die Berechtigung wird
        // gegen genau dieses Turnier geprüft, und der Spieler muss dafür
        // gemeldet sein.
        app.MapGet("/api/tournaments/{tournamentId:guid}/players/{playerId:guid}", async (
            Guid tournamentId,
            Guid playerId,
            IPlayerService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetAsync(tournamentId, playerId, ct))).WithTags("Spieler");

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
