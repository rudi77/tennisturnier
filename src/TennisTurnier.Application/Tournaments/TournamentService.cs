using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

public sealed class TournamentService : ITournamentService
{
    private readonly ITournamentRepository _tournaments;
    private readonly IFormatTemplateRepository _templates;
    private readonly IPlayerRepository _players;
    private readonly IRoleAssignmentRepository _roles;
    private readonly DrawBuilder _drawBuilder;
    private readonly IPublicViewService _publicView;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;

    public TournamentService(
        ITournamentRepository tournaments,
        IFormatTemplateRepository templates,
        IPlayerRepository players,
        IRoleAssignmentRepository roles,
        DrawBuilder drawBuilder,
        IPublicViewService publicView,
        IUnitOfWork unitOfWork,
        IUserContext userContext)
    {
        _tournaments = tournaments;
        _templates = templates;
        _players = players;
        _roles = roles;
        _drawBuilder = drawBuilder;
        _publicView = publicView;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
    }

    private UserPrincipal User => _userContext.Current;

    public async Task<Guid> CreateAsync(
        CreateTournamentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        User.Require(Permission.CreateTournament, ResourceScope.Global);

        var template = await _templates.FindAsync(request.FormatTemplateId, cancellationToken)
            ?? throw new NotFoundException("Formatvorlage", request.FormatTemplateId);

        // Sichtbar heißt nicht verwendbar. Die mitgelieferten Vorlagen sieht
        // jeder — nähme ein Turnier die eigene Vorlage eines anderen Benutzers,
        // hinge sein eingefrorenes Format bis zur Auslosung an einer Definition,
        // die ein Fremder noch ändern kann.
        if (!template.IsBuiltIn && template.OwnerUserId != User.UserId)
        {
            throw new NotFoundException("Formatvorlage", request.FormatTemplateId);
        }

        var tournament = new Tournament(
            Guid.NewGuid(),
            request.Name,
            new Venue(request.VenueName, request.VenueAddress, request.VenueCity, request.TimeZoneId),
            request.Discipline,
            request.StartsOn,
            request.EndsOn,
            template.Id);

        _tournaments.Add(tournament);
        MakeCallerDirectorOf(tournament);

        // Turnier und Rolle in einem Speichervorgang. Das ist keine Feinheit:
        // seit der Query-Filter allein auf den Turnieren mit Rolle steht, wäre
        // ein Turnier ohne seine Zuweisung für den eigenen Anleger im nächsten
        // Augenblick nicht mehr auffindbar — und ohne Rolle gäbe es keinen Weg
        // zurück.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return tournament.Id;
    }

    /// <summary>
    /// Wer ein Turnier anlegt, führt es.
    ///
    /// Ohne Ausnahme für den Systemadministrator: er sieht zwar ohnehin alles,
    /// aber eine Regel mit Ausnahme wäre eine Regel, die für den häufigsten
    /// Aufrufer im Test nie ausgeführt wird — und damit eine, die beim ersten
    /// echten Benutzer zum ersten Mal läuft.
    ///
    /// Der Systemkontext bleibt außen vor: er gehört keinem Menschen, und eine
    /// Rolle für <see cref="Guid.Empty"/> wäre keine.
    /// </summary>
    private void MakeCallerDirectorOf(Tournament tournament)
    {
        if (!User.IsAuthenticated)
        {
            return;
        }

        _roles.Add(new RoleAssignment(
            Guid.NewGuid(),
            User.UserId,
            Role.TournamentDirector,
            ResourceScope.Tournament(tournament.Id)));
    }

    public async Task<IReadOnlyList<TournamentSummary>> ListMineAsync(
        CancellationToken cancellationToken = default) =>
        [.. (await _tournaments.ListForCallerAsync(cancellationToken))
            .Select(t => new TournamentSummary(
                t.Id,
                t.Name,
                t.Venue.Name,
                t.Discipline,
                t.StartsOn,
                t.EndsOn,
                t.State,
                t.SchedulingMode,
                t.AcceptedEntries.Count))];

    public async Task<TournamentDetail> GetAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await Load(tournamentId, cancellationToken);
        var names = await ParticipantNamesAsync(tournament, cancellationToken);

        return new TournamentDetail(
            tournament.Id,
            tournament.Name,
            new VenueDetail(
                tournament.Venue.Name,
                tournament.Venue.Address,
                tournament.Venue.City,
                tournament.Venue.TimeZoneId),
            tournament.Discipline,
            tournament.StartsOn,
            tournament.EndsOn,
            tournament.State,
            tournament.SchedulingMode,
            tournament.FormatTemplateId,
            tournament.Format,
            [.. tournament.Courts
                .OrderBy(court => court.Name, StringComparer.CurrentCulture)
                .Select(Describe)],
            tournament.Entries
                .Select(e => new EntryDetail(
                    e.Id,
                    e.ParticipantId,
                    names.GetValueOrDefault(e.ParticipantId, "(unbekannt)"),
                    e.Seed,
                    e.Status))
                .ToList(),
            tournament.Version);
    }

    private static CourtDetail Describe(TournamentCourt court) => new(
        court.Id,
        court.Name,
        court.Surface,
        court.Location,
        court.IsCenterCourt,
        court.IsActive,
        [.. court.Windows
            .OrderBy(window => window.Period.Start)
            .Select(window => new CourtWindowDetail(window.Id, window.Period.Start, window.Period.End))]);

    public Task UpdateAsync(
        Guid tournamentId,
        UpdateTournamentRequest request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, tournament =>
        {
            tournament.Rename(request.Name);
            tournament.MoveTo(new Venue(
                request.VenueName, request.VenueAddress, request.VenueCity, request.TimeZoneId));
            tournament.ChangeDiscipline(request.Discipline);
            tournament.Reschedule(request.StartsOn, request.EndsOn);
        }, cancellationToken);

    // --- Plätze -----------------------------------------------------------

    public async Task<Guid> AddCourtAsync(
        Guid tournamentId,
        CreateCourtRequest request,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);

        var court = tournament.AddCourt(Guid.NewGuid(), request.Name, request.Surface, request.Location);
        court.MarkAsCenterCourt(request.IsCenterCourt);

        await SaveAndRebuildAsync(tournament, cancellationToken);

        return court.Id;
    }

    public async Task UpdateCourtAsync(
        Guid tournamentId,
        Guid courtId,
        UpdateCourtRequest request,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);

        tournament.RenameCourt(courtId, request.Name);

        var court = tournament.CourtOf(courtId);
        court.MarkAsCenterCourt(request.IsCenterCourt);

        if (request.IsActive)
        {
            court.Reactivate();
        }
        else
        {
            court.Deactivate();
        }

        await SaveAndRebuildAsync(tournament, cancellationToken);
    }

    public async Task RemoveCourtAsync(
        Guid tournamentId,
        Guid courtId,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);
        tournament.RemoveCourt(courtId);

        await SaveAndRebuildAsync(tournament, cancellationToken);
    }

    public async Task<Guid> AddCourtWindowAsync(
        Guid tournamentId,
        Guid courtId,
        CreateCourtWindowRequest request,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);

        var window = tournament.CourtOf(courtId).AddWindow(
            Guid.NewGuid(), new TimeSlot(request.From, request.To));

        tournament.MarkScheduleChanged();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return window.Id;
    }

    public async Task RemoveCourtWindowAsync(
        Guid tournamentId,
        Guid courtId,
        Guid windowId,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);
        tournament.CourtOf(courtId).RemoveWindow(windowId);

        tournament.MarkScheduleChanged();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Legt dieselbe Uhrzeitspanne an jedem Turniertag an — für die genannten
    /// Plätze oder für alle.
    ///
    /// Der Weg, den ein Veranstalter tatsächlich geht: er hat für das ganze
    /// Wochenende zwei Plätze von acht bis zweiundzwanzig zugesagt bekommen. Ihn
    /// dafür zwölf Einzelaufrufe machen zu lassen wäre eine Oberfläche, die den
    /// Datensatz abbildet statt den Vorgang.
    ///
    /// Die Uhrzeiten sind lokal zu verstehen und werden in der Zeitzone der
    /// Anlage aufgelöst — <see cref="LocalTime"/> entscheidet dabei, was an den
    /// zwei Umstellungsnächten im Jahr gilt.
    /// </summary>
    public async Task<int> AddCourtWindowsAsync(
        Guid tournamentId,
        CreateCourtWindowsRequest request,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);

        if (request.To <= request.From)
        {
            throw new DomainException(
                $"Die Platzzeit muss vorwärts laufen, war aber {request.From:HH\\:mm} bis {request.To:HH\\:mm}.");
        }

        var courts = request.CourtIds is { Count: > 0 } ids
            ? [.. ids.Select(tournament.CourtOf)]
            : tournament.Courts.Where(court => court.IsActive).ToList();

        if (courts.Count == 0)
        {
            throw new DomainException("Das Turnier hat keinen Platz, für den sich eine Zeit anlegen ließe.");
        }

        // „An jedem Turniertag" setzt voraus, dass es Turniertage gibt. Ohne
        // Termin liefe die Schleife null Mal und legte wortlos nichts an.
        tournament.RequireDatesRecorded();

        var local = new LocalTime(tournament.Venue.TimeZone);
        var created = 0;

        for (var day = tournament.StartsOn!.Value; day <= tournament.EndsOn!.Value; day = day.AddDays(1))
        {
            var from = local.Resolve(day, request.From, LocalTime.Ambiguity.Earliest);
            var to = local.Resolve(day, request.To, LocalTime.Ambiguity.Latest);

            foreach (var court in courts)
            {
                court.AddWindow(Guid.NewGuid(), new TimeSlot(from, to));
                created++;
            }
        }

        tournament.MarkScheduleChanged();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return created;
    }

    /// <summary>
    /// Name und Zeiten der Plätze stehen in der öffentlichen Ansicht (ADR-0003).
    /// Ohne den Neuaufbau schickt der Aushang die Zuschauer auf einen Platz, der
    /// inzwischen anders heißt.
    /// </summary>
    private async Task SaveAndRebuildAsync(Tournament tournament, CancellationToken cancellationToken)
    {
        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournament.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // --- Zustandsübergänge ------------------------------------------------

    public Task OpenRegistrationAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.OpenRegistration(), cancellationToken);

    public Task CloseRegistrationAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.CloseRegistration(), cancellationToken);

    public async Task GenerateDrawAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);

        var template = await _templates.FindAsync(tournament.FormatTemplateId, cancellationToken)
            ?? throw new NotFoundException("Formatvorlage", tournament.FormatTemplateId);

        tournament.GenerateDraw(template.Definition, template.Version);
        await _drawBuilder.BuildAsync(tournament, cancellationToken);

        // Zwischenspeichern, damit die eben angelegten Phasen für den Aufbau der
        // öffentlichen Ansicht abfragbar sind. Endgültig wird beides erst mit
        // dem Abschluss der Einheit — und zwar gemeinsam.
        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournamentId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Nimmt die Auslosung zurück und verwirft den Draw.
    ///
    /// Beides gehört zusammen: bliebe der Baum stehen, stünden nach einer
    /// Nachmeldung Matches im System, die zu einem Teilnehmerfeld gehören, das
    /// es so nicht mehr gibt.
    /// </summary>
    public async Task ReopenRegistrationAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);

        tournament.ReopenRegistration();
        await _drawBuilder.DiscardAsync(tournamentId, cancellationToken);

        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournamentId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task StartAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.Start(), cancellationToken);

    public Task CompleteAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.Complete(), cancellationToken);

    public Task AbandonAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.Abandon(), cancellationToken);

    public Task SwitchToMatchDayAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.SwitchToMatchDay(), cancellationToken);

    public Task SwitchToPlanningAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.SwitchToPlanning(), cancellationToken);

    // --- Anmeldelink ------------------------------------------------------

    public async Task<RegistrationDetail> GetRegistrationAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);

        return new RegistrationDetail(
            tournament.Registration.Token,
            tournament.Registration.Capacity,
            tournament.Registration.Deadline,
            tournament.Entries.Count(e => e.Status == EntryStatus.Applied),
            tournament.Entries.Count(e => e.Status == EntryStatus.Accepted),
            tournament.Entries.Count(e => e.Status == EntryStatus.WaitingList));
    }

    public Task ConfigureRegistrationAsync(
        Guid tournamentId,
        ConfigureRegistrationRequest request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            tournamentId,
            t => t.ConfigureRegistration(request.Capacity, request.Deadline),
            cancellationToken);

    public Task RotateRegistrationLinkAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.RotateRegistrationLink(), cancellationToken);

    // --- Meldungen --------------------------------------------------------

    /// <summary>
    /// Die Meldungen zur Verwaltung.
    ///
    /// Kontaktdaten und Bestätigungscodes hängen an <c>ViewInternals</c> und
    /// werden andernfalls gar nicht erst geladen: ein Feld, das leer bleibt,
    /// weil es später ausgeblendet wird, ist eine Zeile Code davon entfernt,
    /// doch ausgeliefert zu werden.
    /// </summary>
    public async Task<IReadOnlyList<EntryOverview>> ListEntriesAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default)
    {
        var tournament = await Load(tournamentId, cancellationToken);
        User.Require(Permission.ManageTournament, ResourceScope.Tournament(tournamentId));

        var maySeeInternals = User.Can(Permission.ViewInternals, ResourceScope.Tournament(tournamentId));

        var participants = await _players.FindParticipantsAsync(
            [.. tournament.Entries.Select(e => e.ParticipantId)], cancellationToken);

        var byId = participants.ToDictionary(p => p.Id);

        var contactsByParticipant = maySeeInternals
            ? await ContactsAsync(participants, cancellationToken)
            : [];

        return
        [
            .. tournament.Entries
                .OrderBy(e => e.RegisteredAt)
                .Select(entry => new EntryOverview(
                    entry.Id,
                    entry.ParticipantId,
                    byId.TryGetValue(entry.ParticipantId, out var p) ? p.DisplayName : "(unbekannt)",
                    entry.Seed,
                    entry.Status,
                    entry.Origin,
                    entry.RegisteredAt,
                    maySeeInternals ? entry.ConfirmationCode : null,
                    contactsByParticipant.GetValueOrDefault(entry.ParticipantId, [])))
        ];
    }

    private async Task<Dictionary<Guid, IReadOnlyList<EntryContact>>> ContactsAsync(
        IReadOnlyList<Participant> participants,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, IReadOnlyList<EntryContact>>();

        foreach (var participant in participants)
        {
            var contacts = new List<EntryContact>();

            foreach (var playerId in participant.PlayerIds)
            {
                if (await _players.FindAsync(playerId, cancellationToken) is { } player)
                {
                    contacts.Add(new EntryContact(
                        player.Id, player.DisplayName, player.Contact.Email, player.Contact.Phone));
                }
            }

            result[participant.Id] = contacts;
        }

        return result;
    }

    public async Task<Guid> EnterAsync(
        Guid tournamentId,
        EnterTournamentRequest request,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);

        var participant = await _players.FindParticipantAsync(request.ParticipantId, cancellationToken)
            ?? throw new NotFoundException("Teilnehmer", request.ParticipantId);

        // Die Disziplin steht in der Ausschreibung, der Teilnehmer entsteht
        // davon unabhängig. Hier treffen sie zum ersten Mal aufeinander — und
        // hier gehört die Prüfung hin, nicht ins Anlegen des Teilnehmers, der
        // von keinem Turnier weiß.
        tournament.RequireMatchesDiscipline(participant.IsTeam);

        var entry = tournament.Enter(Guid.NewGuid(), request.ParticipantId, request.Seed);
        var entryId = entry.Id;

        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournamentId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return entryId;
    }

    public Task AcceptAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.Accept(entryId), cancellationToken);

    public Task MoveToWaitingListAsync(
        Guid tournamentId,
        Guid entryId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.MoveToWaitingList(entryId), cancellationToken);

    public Task WithdrawAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.Withdraw(entryId), cancellationToken);

    public Task SetSeedAsync(
        Guid tournamentId,
        Guid entryId,
        SetSeedRequest request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(tournamentId, t => t.SetSeed(entryId, request.Seed), cancellationToken);

    // --- Innere Helfer ----------------------------------------------------

    /// <summary>
    /// Lädt, prüft die Berechtigung, wendet die Änderung an und speichert.
    ///
    /// Die Berechtigung wird <em>nach</em> dem Laden geprüft, damit ein Turnier
    /// außerhalb des Scopes als 404 endet und nicht als 403 — ein 403 verriete
    /// seine Existenz (ADR-0004).
    /// </summary>
    private async Task MutateAsync(
        Guid tournamentId,
        Action<Tournament> change,
        CancellationToken cancellationToken)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);
        change(tournament);
        // Jede Änderung am Turnier kann die öffentliche Ansicht betreffen — der
        // Name, die Termine, der Zustand, eine Setzposition. Statt zu erraten,
        // welche es tut, wird immer neu gebaut; ob dabei etwas herauskommt,
        // entscheidet der Vergleich in der Projektion (ADR-0003). Beides geht in
        // einem Zug in die Datenbank, sonst können sie auseinanderlaufen.
        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournamentId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Tournament> Load(Guid tournamentId, CancellationToken cancellationToken) =>
        await _tournaments.FindAsync(tournamentId, cancellationToken)
        ?? throw new NotFoundException("Turnier", tournamentId);

    private async Task<Tournament> LoadForManagement(Guid tournamentId, CancellationToken cancellationToken)
    {
        var tournament = await Load(tournamentId, cancellationToken);

        User.Require(
            Permission.ManageTournament,
            ResourceScope.Tournament(tournamentId));

        return tournament;
    }

    private async Task<Dictionary<Guid, string>> ParticipantNamesAsync(
        Tournament tournament,
        CancellationToken cancellationToken)
    {
        var ids = tournament.Entries.Select(e => e.ParticipantId).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var participants = await _players.FindParticipantsAsync(ids, cancellationToken);
        return participants.ToDictionary(p => p.Id, p => p.DisplayName);
    }
}
