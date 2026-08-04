using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

/// <summary>
/// Der Turniertagbetrieb: Warteschlangen je Platz, Aufrufen, Starten, Beenden,
/// Unterbrechen (ADR-0002).
/// </summary>
public interface ICourtQueueService
{
    /// <summary>Die aktuelle Belegung aller Plätze samt Warteschlange.</summary>
    Task<IReadOnlyList<CourtBoard>> GetBoardAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default);

    Task CallAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    Task StartAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Beendet die Platzbelegung. Das Ergebnis wird davon getrennt eingetragen —
    /// der Platz ist frei, sobald die Spieler ihn verlassen, und nicht erst,
    /// wenn jemand Zeit hatte, den Zettel auszufüllen.
    /// </summary>
    Task FinishAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    Task SuspendAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>Setzt eine unterbrochene Partie fort, wahlweise auf einem anderen Platz.</summary>
    Task<Guid> ResumeAsync(
        Guid assignmentId,
        ResumeMatchRequest request,
        CancellationToken cancellationToken = default);

    Task ReorderAsync(
        Guid tournamentId,
        Guid courtId,
        ReorderQueueRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Sagt einem wartenden Match eine früheste Startzeit zu.</summary>
    Task PromiseAsync(
        Guid assignmentId,
        PromiseStartRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CourtQueueService : ICourtQueueService
{
    private readonly ITournamentRepository _tournaments;
    private readonly ICourtAssignmentRepository _assignments;
    private readonly IClubRepository _clubs;
    private readonly IPlayerRepository _players;
    private readonly IPublicViewService _publicView;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CourtQueueService(
        ITournamentRepository tournaments,
        ICourtAssignmentRepository assignments,
        IClubRepository clubs,
        IPlayerRepository players,
        IPublicViewService publicView,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _tournaments = tournaments;
        _assignments = assignments;
        _clubs = clubs;
        _players = players;
        _publicView = publicView;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CourtBoard>> GetBoardAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadTournamentAsync(tournamentId, cancellationToken);
        var club = await LoadClubAsync(tournament, cancellationToken);

        var assignments = await _assignments.ListByTournamentAsync(tournamentId, cancellationToken);
        var matches = await _assignments.ListMatchesAsync(tournamentId, cancellationToken);
        var names = await NamesByEntryAsync(tournament, cancellationToken);

        var calendar = new CourtCalendar(club.TimeZone);
        var range = calendar.TournamentRange(tournament.StartsOn, tournament.EndsOn);

        return
        [
            .. club.Courts
                .Where(court => court.IsActive || assignments.Any(a => a.CourtId == court.Id && !a.IsOver))
                .OrderBy(court => court.Name, StringComparer.CurrentCulture)
                .Select(court => Board(
                    court,
                    assignments,
                    matches,
                    names,
                    MatchOrigins.LabelsOf(matches),
                    calendar.FreeWindows(court, range))),
        ];
    }

    // --- Tagesbetrieb ------------------------------------------------------

    public Task CallAsync(Guid assignmentId, CancellationToken cancellationToken = default) =>
        OnMatchDayAsync(
            assignmentId, assignment => assignment.Call(), cancellationToken, requireCourtFree: true);

    public Task StartAsync(Guid assignmentId, CancellationToken cancellationToken = default) =>
        OnMatchDayAsync(
            assignmentId,
            assignment => assignment.Start(_clock.Now),
            cancellationToken,
            requireCourtFree: true);

    public Task FinishAsync(Guid assignmentId, CancellationToken cancellationToken = default) =>
        OnMatchDayAsync(assignmentId, assignment => assignment.Finish(_clock.Now), cancellationToken);

    public Task SuspendAsync(Guid assignmentId, CancellationToken cancellationToken = default) =>
        OnMatchDayAsync(assignmentId, assignment => assignment.Suspend(), cancellationToken);

    public async Task<Guid> ResumeAsync(
        Guid assignmentId,
        ResumeMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (tournament, club, assignment) = await LoadForDayAsync(assignmentId, cancellationToken);

        // Auf welchem Platz weitergespielt wird, entscheidet die Turnierleitung.
        RequireManagePermission(tournament);

        if (assignment.Status != AssignmentStatus.Suspended)
        {
            throw new DomainException(
                $"Fortgesetzt wird eine unterbrochene Partie; diese ist {assignment.Status}.");
        }

        await RequireReadyAsync(assignment, cancellationToken);

        var resumed = assignment;

        if (request.CourtId is { } courtId && courtId != assignment.CourtId)
        {
            var court = club.Courts.FirstOrDefault(c => c.Id == courtId && c.IsActive)
                ?? throw new NotFoundException("Platz", courtId);

            // Die unterbrochene Zuweisung wird abgeschlossen und bleibt als
            // Historie stehen — erst beide zusammen erzählen, was an diesem Tag
            // passiert ist (ADR-0002). Abgeschlossen ausdrücklich: bliebe sie
            // unterbrochen, ließe sie sich ein zweites Mal fortsetzen, und das
            // Match liefe auf zwei Plätzen gleichzeitig.
            assignment.Finish(_clock.Now);

            resumed = new CourtAssignment(
                Guid.NewGuid(),
                tournament.Id,
                assignment.MatchId,
                court.Id,
                NextSequence(await _assignments.ListByTournamentAsync(tournament.Id, cancellationToken), court.Id),
                assignment.EstimatedDuration,
                assignment.Source);

            _assignments.Add(resumed);
        }

        resumed.Start(_clock.Now);

        // Beide Plätze nachziehen: der alte ist frei geworden, der neue ist belegt.
        await ReflowCourtAsync(tournament.Id, assignment.CourtId, cancellationToken);

        if (resumed.CourtId != assignment.CourtId)
        {
            await ReflowCourtAsync(tournament.Id, resumed.CourtId, cancellationToken);
        }

        await SaveAsync(tournament, cancellationToken);

        return resumed.Id;
    }

    public async Task ReorderAsync(
        Guid tournamentId,
        Guid courtId,
        ReorderQueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AssignmentIds is null)
        {
            throw new DomainException("Die neue Reihenfolge nennt keine Zuweisungen.");
        }

        var tournament = await LoadTournamentAsync(tournamentId, cancellationToken);
        RequireManagePermission(tournament);

        // Auch das Umstellen rechnet ab „jetzt" — im Planungsmodus zerstörte es
        // den gerechneten Spielplan, ohne inhaltlich etwas zu ändern.
        RequireMatchDay(tournament);

        var assignments = await _assignments.ListByTournamentAsync(tournamentId, cancellationToken);
        var onCourt = assignments.Where(a => a.CourtId == courtId).ToList();

        if (onCourt.Count == 0)
        {
            throw new NotFoundException("Platz", courtId);
        }

        CourtQueue.Reorder(onCourt, request.AssignmentIds);
        CourtQueue.Reflow(onCourt, CourtQueue.FreeFrom(onCourt, _clock.Now), _clock.Now);

        await SaveAsync(tournament, cancellationToken);
    }

    public async Task PromiseAsync(
        Guid assignmentId,
        PromiseStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (tournament, _, assignment) = await LoadForDayAsync(assignmentId, cancellationToken);

        // Eine Zusage verschiebt alles dahinter — das ist eine Dispositions-
        // entscheidung und keine Ergebniseingabe.
        RequireManagePermission(tournament);

        if (assignment.IsOver)
        {
            throw new DomainException("Einer beendeten Zuweisung lässt sich nichts mehr zusagen.");
        }

        tournament.RequireScheduledWithin(request.EarliestStart);
        assignment.PromiseNotBefore(request.EarliestStart);

        await ReflowCourtAsync(tournament.Id, assignment.CourtId, cancellationToken);
        await SaveAsync(tournament, cancellationToken);
    }

    // --- Innere Helfer -----------------------------------------------------

    private async Task OnMatchDayAsync(
        Guid assignmentId,
        Action<CourtAssignment> change,
        CancellationToken cancellationToken,
        bool requireCourtFree = false)
    {
        var (tournament, _, assignment) = await LoadForDayAsync(assignmentId, cancellationToken);

        if (requireCourtFree)
        {
            await RequireReadyAsync(assignment, cancellationToken);
            await RequireCourtFreeAsync(assignment, cancellationToken);
            RequirePromiseKept(assignment);
        }

        change(assignment);

        // Nach jeder Zustandsänderung die Schätzungen der Wartenden nachziehen:
        // ein Match, das eine halbe Stunde länger dauert, verschiebt alles
        // dahinter, und ein Aushang, der das verschweigt, ist unbrauchbar.
        await ReflowCourtAsync(tournament.Id, assignment.CourtId, cancellationToken);
        await SaveAsync(tournament, cancellationToken);
    }

    /// <summary>
    /// Ein Match darf erst aufgerufen werden, wenn feststeht, wer antritt.
    ///
    /// Eingeplant wird der ganze Baum, lange bevor die Teilnehmer bekannt sind —
    /// das ist im Planungsmodus richtig und kein Fehler. Am Platz aufgerufen wird
    /// aber nicht eine Referenz, sondern ein Mensch. Diese Grenze setzt der
    /// Tagesbetrieb, nicht die Spielplanprüfung (ADR-0002).
    /// </summary>
    private async Task RequireReadyAsync(CourtAssignment assignment, CancellationToken cancellationToken)
    {
        var matches = await _assignments.ListMatchesAsync(assignment.TournamentId, cancellationToken);
        var match = matches.FirstOrDefault(m => m.Id == assignment.MatchId)
            ?? throw new NotFoundException("Match", assignment.MatchId);

        if (match.Status == MatchStatus.Pending)
        {
            throw new DomainException(
                $"„{match}“ steht noch nicht fest — es wartet auf sein Vorspiel und lässt sich " +
                "deshalb nicht aufrufen.");
        }

        if (match.Status == MatchStatus.Finished)
        {
            throw new DomainException("Dieses Match ist bereits entschieden.");
        }
    }

    /// <summary>
    /// Auf einem Platz wird ein Match gespielt, nicht zwei.
    ///
    /// Die Warteschlange sagt, wer als Nächstes drankommt; sie hindert aber
    /// niemanden daran, ein wartendes Match unmittelbar aufzurufen. Ohne diese
    /// Prüfung stünden zwei Zuweisungen desselben Platzes auf <c>Running</c>, die
    /// Platzübersicht zeigte nur eine davon als die laufende, und die andere wäre
    /// weder sichtbar noch zu beenden — die Historie des Platzes behauptete
    /// dauerhaft zwei gleichzeitige Partien.
    /// </summary>
    private async Task RequireCourtFreeAsync(CourtAssignment assignment, CancellationToken cancellationToken)
    {
        var onCourt = await _assignments.ListByTournamentAsync(assignment.TournamentId, cancellationToken);

        var occupying = onCourt.FirstOrDefault(other =>
            other.Id != assignment.Id
            && other.CourtId == assignment.CourtId
            && other.Status is AssignmentStatus.Called or AssignmentStatus.Running);

        if (occupying is not null)
        {
            throw new DomainException(
                "Auf diesem Platz steht bereits eine Partie. Sie zuerst beenden oder unterbrechen.");
        }
    }

    /// <summary>
    /// Eine Zusage gilt auch beim Aufruf.
    ///
    /// „Nicht vor 14 Uhr" ist das Einzige, worauf sich ein Spieler verlassen kann
    /// (ADR-0002). Die Warteschlange zieht ihre Schätzungen entsprechend nach —
    /// wer die Zuweisung aber unmittelbar aufruft, ginge daran vorbei, und der
    /// Spieler, der sich auf die Zusage verlassen hat, ist nicht da. Soll früher
    /// begonnen werden, wird zuerst die Zusage geändert; das ist eine Entscheidung
    /// und keine Nebenwirkung.
    /// </summary>
    private void RequirePromiseKept(CourtAssignment assignment)
    {
        if (assignment.EarliestStart is { } promised && _clock.Now < promised)
        {
            throw new DomainException(
                $"Diesem Match wurde „nicht vor {promised:t}“ zugesagt. Für einen früheren Aufruf " +
                "zuerst die Zusage ändern.");
        }
    }

    private async Task ReflowCourtAsync(Guid tournamentId, Guid courtId, CancellationToken cancellationToken)
    {
        var onCourt = (await _assignments.ListByTournamentAsync(tournamentId, cancellationToken))
            .Where(assignment => assignment.CourtId == courtId)
            .ToList();

        CourtQueue.Reflow(onCourt, CourtQueue.FreeFrom(onCourt, _clock.Now), _clock.Now);
    }

    private async Task SaveAsync(Tournament tournament, CancellationToken cancellationToken)
    {
        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournament.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static int NextSequence(IReadOnlyList<CourtAssignment> assignments, Guid courtId) =>
        assignments.Where(a => a.CourtId == courtId).Select(a => a.SequenceOnCourt).DefaultIfEmpty(0).Max() + 1;

    private async Task<(Tournament Tournament, Club Club, CourtAssignment Assignment)> LoadForDayAsync(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var assignment = await _assignments.FindAsync(assignmentId, cancellationToken)
            ?? throw new NotFoundException("Platzzuweisung", assignmentId);

        var tournament = await LoadTournamentAsync(assignment.TournamentId, cancellationToken);

        // Aufrufen und Starten darf auch der Schiedsrichter — er steht am Platz.
        _userContext.Current.Require(
            Permission.EnterResults,
            ResourceScope.Tournament(tournament.Id),
            ResourceScope.Club(tournament.ClubId));

        RequireMatchDay(tournament);

        return (tournament, await LoadClubAsync(tournament, cancellationToken), assignment);
    }

    private async Task<Tournament> LoadTournamentAsync(Guid tournamentId, CancellationToken cancellationToken) =>
        await _tournaments.FindAsync(tournamentId, cancellationToken)
        ?? throw new NotFoundException("Turnier", tournamentId);

    private async Task<Club> LoadClubAsync(Tournament tournament, CancellationToken cancellationToken) =>
        await _clubs.FindAsync(tournament.ClubId, cancellationToken)
        ?? throw new NotFoundException("Verein", tournament.ClubId);

    /// <summary>
    /// Aufrufen, Starten und Beenden gehören in den Turniertagbetrieb.
    ///
    /// Der Wechsel dorthin ist ein ausdrücklicher Schritt, weil er die Bedeutung
    /// jeder angezeigten Uhrzeit ändert: aus einer Schätzung wird eine Zusage
    /// (ADR-0002). Wer im Planungsmodus aufruft, sagt damit unabsichtlich zu,
    /// was er nur geschätzt hat.
    /// </summary>
    private static void RequireMatchDay(Tournament tournament)
    {
        if (tournament.SchedulingMode != SchedulingMode.MatchDay)
        {
            throw new DomainException(
                "Das Turnier ist im Planungsmodus. Für den Tagesbetrieb zuerst ausdrücklich " +
                "in den Turniertagbetrieb wechseln.");
        }
    }

    private void RequireManagePermission(Tournament tournament) =>
        _userContext.Current.Require(
            Permission.ManageTournament,
            ResourceScope.Tournament(tournament.Id),
            ResourceScope.Club(tournament.ClubId));

    // --- Abbildung ---------------------------------------------------------

    private static CourtBoard Board(
        Court court,
        IReadOnlyList<CourtAssignment> assignments,
        IReadOnlyList<Match> matches,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<Guid, string> labels,
        IReadOnlyList<TimeSlot> openingHours)
    {
        var onCourt = assignments.Where(a => a.CourtId == court.Id).ToList();

        // Das laufende Match zuerst. Ein eben aufgerufenes daneben ist der
        // Normalfall — die Spieler sind auf dem Weg —, es ist aber nicht das,
        // was auf dem Platz steht.
        var current = onCourt
            .Where(a => a.Status is AssignmentStatus.Called or AssignmentStatus.Running)
            .OrderBy(CourtQueue.Liveness)
            .FirstOrDefault();

        // Und alles Übrige, das noch ansteht, in der Warteschlange — auch das
        // schon Aufgerufene. Verschwände es hier, ließe es sich über die
        // Platzübersicht nicht mehr erreichen.
        var queue = onCourt
            .Where(a => a.Id != current?.Id
                && a.Status is AssignmentStatus.Called or AssignmentStatus.Planned)
            .OrderBy(CourtQueue.Liveness)
            .ThenBy(a => a.SequenceOnCourt)
            .ToList();

        return new CourtBoard(
            court.Id,
            court.Name,
            court.IsCenterCourt,
            current is null ? null : Describe(current, matches, names, labels, openingHours),
            [.. queue.Select(a => Describe(a, matches, names, labels, openingHours))]);
    }

    private static QueuedMatch Describe(
        CourtAssignment assignment,
        IReadOnlyList<Match> matches,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<Guid, string> labels,
        IReadOnlyList<TimeSlot> openingHours)
    {
        var match = matches.FirstOrDefault(m => m.Id == assignment.MatchId);

        return new QueuedMatch(
            assignment.Id,
            assignment.MatchId,
            match?.Label,
            NameOf(match?.Side1, names, labels),
            NameOf(match?.Side2, names, labels),
            assignment.SequenceOnCourt,
            assignment.Status,
            match?.Status ?? MatchStatus.Pending,
            assignment.EarliestStart,
            assignment.PlannedStart,
            assignment.ActualStart,
            assignment.EstimatedDuration,
            FitsOpeningHours(assignment, openingHours),
            assignment.Version);
    }

    /// <summary>
    /// Passt das geschätzte Zeitfenster vollständig in eine Öffnungszeit? Ohne
    /// Schätzung gibt es nichts zu beurteilen — dann gilt es als in Ordnung.
    /// </summary>
    private static bool FitsOpeningHours(CourtAssignment assignment, IReadOnlyList<TimeSlot> openingHours) =>
        assignment.PlannedSlot is not { } slot
        || openingHours.Any(window => window.Start <= slot.Start && slot.End <= window.End);

    /// <summary>
    /// Der Name auf der Karte — oder, solange niemand feststeht, die Herkunft in
    /// Worten. „Sieger aus Halbfinale 1" und nicht die Kennung des Vorspiels:
    /// die Karte hängt am Turniertag an der Platzwand.
    /// </summary>
    private static string? NameOf(
        MatchSide? side,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<Guid, string> labels) =>
        side is null
            ? null
            : side.EntryId is { } entryId
                ? names.GetValueOrDefault(entryId)
                : MatchOrigins.Describe(side.Origin, labels);

    private async Task<IReadOnlyDictionary<Guid, string>> NamesByEntryAsync(
        Tournament tournament,
        CancellationToken cancellationToken)
    {
        var participantIds = tournament.Entries.Select(e => e.ParticipantId).Distinct().ToList();
        var participants = await _players.FindParticipantsAsync(participantIds, cancellationToken);
        var byParticipant = participants.ToDictionary(p => p.Id, p => p.DisplayName);

        return tournament.Entries.ToDictionary(
            entry => entry.Id,
            entry => byParticipant.GetValueOrDefault(entry.ParticipantId, "(unbekannt)"));
    }
}
