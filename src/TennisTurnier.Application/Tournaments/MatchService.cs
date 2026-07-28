using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

/// <summary>Matches, Ergebnisse und die Zuweisung von Plätzen.</summary>
public interface IMatchService
{
    Task<IReadOnlyList<PhaseDetail>> GetPhasesAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default);

    Task<StandingsDetail> GetStandingsAsync(
        Guid tournamentId,
        Guid phaseId,
        CancellationToken cancellationToken = default);

    Task RecordResultAsync(
        Guid matchId,
        RecordResultRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Nimmt ein Ergebnis zurück — für die Korrektur eines Eingabefehlers.</summary>
    Task ClearResultAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task<AssignCourtResult> AssignCourtAsync(
        Guid matchId,
        AssignCourtRequest request,
        CancellationToken cancellationToken = default);

    Task RemoveAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default);
}

public sealed class MatchService : IMatchService
{
    private static readonly TimeSpan DefaultMatchDuration = TimeSpan.FromMinutes(90);

    /// <summary>
    /// Voreinstellung der Mindestpause. ADR-0002 nennt 30 Minuten; ab M6 kommt
    /// sie aus der Turnierkonfiguration.
    /// </summary>
    private static readonly TimeSpan DefaultRest = TimeSpan.FromMinutes(30);

    private readonly ITournamentRepository _tournaments;
    private readonly IPhaseRepository _phases;
    private readonly ICourtAssignmentRepository _assignments;
    private readonly IClubRepository _clubs;
    private readonly IPlayerRepository _players;
    private readonly IPublicViewService _publicView;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public MatchService(
        ITournamentRepository tournaments,
        IPhaseRepository phases,
        ICourtAssignmentRepository assignments,
        IClubRepository clubs,
        IPlayerRepository players,
        IPublicViewService publicView,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _tournaments = tournaments;
        _phases = phases;
        _assignments = assignments;
        _clubs = clubs;
        _players = players;
        _publicView = publicView;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<IReadOnlyList<PhaseDetail>> GetPhasesAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadTournament(tournamentId, cancellationToken);
        var phases = await _phases.ListByTournamentAsync(tournamentId, cancellationToken);
        var context = await DescribeAsync(tournament, cancellationToken);

        return phases
            .OrderBy(p => p.Ordinal)
            .Select(p => new PhaseDetail(
                p.Id,
                p.Ordinal,
                p.Name,
                p.Status,
                p.Matches
                    .OrderBy(m => m.Round)
                    .ThenBy(m => m.Position)
                    .Select(m => Describe(m, context))
                    .ToList()))
            .ToList();
    }

    public async Task<StandingsDetail> GetStandingsAsync(
        Guid tournamentId,
        Guid phaseId,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadTournament(tournamentId, cancellationToken);
        var phases = await _phases.ListByTournamentAsync(tournamentId, cancellationToken);

        var phase = phases.FirstOrDefault(p => p.Id == phaseId)
            ?? throw new NotFoundException("Phase", phaseId);

        var state = await BuildStateAsync(tournament, phase, cancellationToken);

        return new StandingsDetail(phaseId, PhaseFormats.For(phase.Format).ComputeStandings(state).Places);
    }

    public async Task RecordResultAsync(
        Guid matchId,
        RecordResultRequest request,
        CancellationToken cancellationToken = default)
    {
        var (tournament, phases, phase, match) = await LoadForResultAsync(matchId, cancellationToken);

        RequireResultPermission(tournament);

        // Ein bereits eingetragenes Ergebnis zu überschreiben ist eine Korrektur
        // und keine Eingabe — sie kann eine Folgephase umbesetzen.
        if (match.Score is not null)
        {
            RequireLaterPhasesUntouched(tournament, phases, phase);
        }

        var matchFormat = MatchFormatOf(tournament, phase);
        phase.RecordResult(matchId, BuildScore(request, match, matchFormat));

        // Das erste Ergebnis macht aus einem ausgelosten ein laufendes Turnier.
        if (tournament.State == TournamentState.DrawGenerated)
        {
            tournament.Start();
        }

        await ReleaseQueueAsync(tournament.Id, matchId, cancellationToken);
        await AdvancePhasesAsync(tournament, phases, cancellationToken);

        // Zwischenspeichern, bevor die öffentliche Ansicht entsteht: sie soll den
        // Stand der Datenbank abbilden und nicht die Kopien vom Anfang des
        // Requests, in denen die Ergebnisse paralleler Eingaben fehlen.
        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournament.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Gibt den Platz frei, den ein entschiedenes Match noch belegt.
    ///
    /// Nicht jedes Match wird am Platz aufgerufen: ein Nichtantreten wird
    /// eingetragen, ohne dass jemand hingeht. Bliebe die Zuweisung stehen,
    /// behielte sie ihre Nummer in der Warteschlange, blockierte anderthalb
    /// Stunden für alles dahinter und stünde öffentlich als wartendes Match —
    /// und wäre über den Turniertag nicht mehr loszuwerden, weil sich weder
    /// aufrufen noch beenden lässt, was schon entschieden ist.
    ///
    /// Aufgerufene, laufende und unterbrochene Zuweisungen bleiben unangetastet:
    /// sie gehören dem Tagesbetrieb, und ihre Historie ist der Grund, warum die
    /// Zuweisung eine eigene Entität ist (ADR-0002).
    /// </summary>
    private async Task ReleaseQueueAsync(
        Guid tournamentId,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var all = await _assignments.ListByTournamentAsync(tournamentId, cancellationToken);
        var waiting = all
            .Where(a => a.MatchId == matchId && a.Status == AssignmentStatus.Planned)
            .ToList();

        foreach (var assignment in waiting)
        {
            _assignments.Remove(assignment);
        }

        // Die betroffenen Plätze rücken nach: sonst bliebe eine Lücke in der
        // Nummerierung, und die wird am Platz vorgelesen.
        foreach (var courtId in waiting.Select(a => a.CourtId).Distinct())
        {
            var onCourt = all
                .Where(a => a.CourtId == courtId && !waiting.Contains(a))
                .ToList();

            CourtQueue.Reflow(onCourt, CourtQueue.FreeFrom(onCourt, _clock.Now), _clock.Now);
        }
    }

    public async Task ClearResultAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var (tournament, phases, phase, _) = await LoadForResultAsync(matchId, cancellationToken);

        RequireResultPermission(tournament);
        RequireLaterPhasesUntouched(tournament, phases, phase);

        phase.ClearResult(matchId);

        // Auch nach einer Rücknahme: eine Folgephase, die daraufhin nicht mehr
        // vollständig besetzt ist, muss ihre offenen Plätze zurückbekommen.
        await AdvancePhasesAsync(tournament, phases, cancellationToken);

        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournament.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Weist eine Korrektur zurück, wenn eine Folgephase bereits gespielt hat.
    ///
    /// Dieselbe Regel wie innerhalb einer Phase, nur eine Ebene höher: eine Kette
    /// wird von hinten aufgerollt. Ohne sie ließe sich ein Gruppenergebnis
    /// umdrehen, während das Finale längst gespielt ist — Tabelle und Baum
    /// widersprächen sich dauerhaft, und ein Turniersieger stünde fest, der laut
    /// korrigierter Tabelle nie hätte antreten dürfen.
    /// </summary>
    private static void RequireLaterPhasesUntouched(
        Tournament tournament,
        IReadOnlyList<Phase> phases,
        Phase phase)
    {
        if (tournament.Format?.Definition is not { } definition)
        {
            return;
        }

        var dependent = phases.FirstOrDefault(other =>
            other.Id != phase.Id
            && other.HasAnyResult
            && PhaseOrchestrator.DefinitionOf(definition, other)?.Qualification?.FromPhase == phase.Ordinal);

        if (dependent is not null)
        {
            throw new DomainException(
                $"Das Ergebnis lässt sich nicht ändern, weil in „{dependent.Name}“ bereits gespielt wurde. " +
                "Zuerst die Ergebnisse dieser Phase zurücknehmen.");
        }
    }

    /// <summary>
    /// Reicht abgeschlossene Phasen an die jeweils folgende weiter (ADR-0001).
    /// </summary>
    private async Task AdvancePhasesAsync(
        Tournament tournament,
        IReadOnlyList<Phase> phases,
        CancellationToken cancellationToken) =>
        PhaseOrchestrator.Advance(
            tournament, phases, await NamesByEntryAsync(tournament, cancellationToken));

    public async Task<AssignCourtResult> AssignCourtAsync(
        Guid matchId,
        AssignCourtRequest request,
        CancellationToken cancellationToken = default)
    {
        // Das Match wird über die Phase geladen: es muss existieren und zu einem
        // sichtbaren Turnier gehören, bevor ihm ein Platz zugewiesen wird.
        var (tournament, _, _, _) = await LoadForResultAsync(matchId, cancellationToken);
        RequireManagePermission(tournament);

        var club = await _clubs.FindAsync(tournament.ClubId, cancellationToken)
            ?? throw new NotFoundException("Verein", tournament.ClubId);

        var court = club.Courts.FirstOrDefault(c => c.Id == request.CourtId)
            ?? throw new NotFoundException("Platz", request.CourtId);

        // Ein stillgelegter Platz taucht in der öffentlichen Platzliste nicht auf.
        // Ein Match darauf anzusetzen hieße, es an einen Ort zu schicken, den
        // niemand angezeigt bekommt.
        if (!court.IsActive)
        {
            throw new DomainException(
                $"Der Platz „{court.Name}“ ist stillgelegt und lässt sich nicht belegen.");
        }

        var existing = await _assignments.ListByTournamentAsync(tournament.Id, cancellationToken);

        var duration = request.EstimatedDuration ?? DefaultMatchDuration;
        var source = request.Pinned ? AssignmentSource.Pinned : AssignmentSource.Manual;

        // Eine erneute Zuweisung desselben Matches plant die bestehende um,
        // solange sie noch nicht aufgerufen wurde. Sobald sie läuft, unterbrochen
        // oder beendet ist, bleibt sie stehen — sie ist dann Teil der Historie des
        // Turniertags, und eine Fortsetzung auf einem anderen Platz ist eine
        // eigene Zuweisung (ADR-0002).
        var assignment = existing.FirstOrDefault(
            a => a.MatchId == matchId && a.Status == AssignmentStatus.Planned);

        if (assignment is null)
        {
            assignment = new CourtAssignment(
                Guid.NewGuid(), tournament.Id, matchId, court.Id, request.SequenceOnCourt, duration, source);

            _assignments.Add(assignment);
        }

        assignment.Replan(
            court.Id, request.SequenceOnCourt, request.PlannedStart, request.EarliestStart, duration, source);

        // Erst prüfen, dann speichern: scheitert die Prüfung, sieht der Aufrufer
        // sonst einen Fehler zu einer Zuweisung, die längst geschrieben und an
        // die Zuschauer gemeldet ist.
        var violations = await ValidateAsync(
            tournament,
            club,
            existing.Where(a => a.Id != assignment.Id).Append(assignment).ToList(),
            cancellationToken);

        // Zwischenspeichern, damit die neue Zuweisung beim Aufbau der
        // öffentlichen Ansicht abfragbar ist.
        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournament.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AssignCourtResult(assignment.Id, violations);
    }

    public async Task RemoveAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await _assignments.FindAsync(assignmentId, cancellationToken)
            ?? throw new NotFoundException("Platzzuweisung", assignmentId);

        var tournament = await LoadTournament(assignment.TournamentId, cancellationToken);
        RequireManagePermission(tournament);

        _assignments.Remove(assignment);

        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournament.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // --- Ergebnisaufbau ----------------------------------------------------

    /// <summary>
    /// Baut aus der Anfrage ein gültiges Ergebnis.
    ///
    /// Die Zuordnung „welche Seite ist betroffen" ist der Grund, warum die
    /// Anfrage eine Seite trägt: bei Aufgabe, Nichtantreten und Disqualifikation
    /// ist nicht der Sieger anzugeben, sondern der, dem etwas widerfahren ist.
    /// </summary>
    private static Score BuildScore(RecordResultRequest request, Match match, MatchFormat format)
    {
        var sets = request.Sets ?? [];

        return request.Outcome switch
        {
            MatchOutcome.Normal => Score.Played(sets, format),
            MatchOutcome.Retirement => Score.Retired(sets, request.AbandonedSet, RequireSide(request), format),
            MatchOutcome.Walkover => Score.Walkover(RequireSide(request)),
            MatchOutcome.Disqualification => Score.Disqualified(RequireSide(request)),
            MatchOutcome.Bye => throw new DomainException(
                "Ein Freilos wird beim Aufbau des Baums entschieden und nicht eingetragen."),
            _ => throw new DomainException($"Unbekannter Ausgang {request.Outcome} für Match {match.Id}."),
        };
    }

    private static int RequireSide(RecordResultRequest request) =>
        request.AffectedSide
        ?? throw new DomainException(
            $"Für den Ausgang {request.Outcome} muss angegeben werden, welche Seite betroffen ist.");

    private static MatchFormat MatchFormatOf(Tournament tournament, Phase phase)
    {
        var definition = tournament.Format?.Definition
            ?? throw new DomainException("Das Turnier hat kein eingefrorenes Format.");

        var phaseDefinition = definition.Phases.FirstOrDefault(p => p.Ordinal == phase.Ordinal)
            ?? throw new DomainException($"Das Format kennt keine Phase {phase.Ordinal}.");

        return definition.MatchFormatOf(phaseDefinition);
    }

    // --- Spielplanprüfung --------------------------------------------------

    private async Task<IReadOnlyList<ScheduleViolationDetail>> ValidateAsync(
        Tournament tournament,
        Club club,
        IReadOnlyList<CourtAssignment> assignments,
        CancellationToken cancellationToken)
    {
        var matches = await _assignments.ListMatchesAsync(tournament.Id, cancellationToken);
        var playersByEntry = await PlayersByEntryAsync(tournament, cancellationToken);

        var calendar = new CourtCalendar(club.TimeZone);
        var range = calendar.TournamentRange(tournament.StartsOn, tournament.EndsOn);

        var windows = club.Courts.ToDictionary(
            court => court.Id,
            court => calendar.FreeWindows(court, range));

        var context = new SchedulingContext(matches, playersByEntry, windows, DefaultRest);

        return new ScheduleValidator()
            .Validate(assignments, context)
            .Select(v => new ScheduleViolationDetail(v.Constraint, v.Message, v.AssignmentId))
            .ToList();
    }

    /// <summary>
    /// Von der Meldung zu den dahinterstehenden Spielern.
    ///
    /// Der Umweg ist nötig, weil ein Doppel zwei Spieler hat und derselbe Spieler
    /// in mehreren Meldungen stecken kann — wer im Einzel und im Doppel antritt,
    /// wäre über die Meldung allein nicht als Doppelbelegung zu erkennen.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> PlayersByEntryAsync(
        Tournament tournament,
        CancellationToken cancellationToken)
    {
        var participantIds = tournament.Entries.Select(e => e.ParticipantId).Distinct().ToList();
        var participants = await _players.FindParticipantsAsync(participantIds, cancellationToken);
        var byId = participants.ToDictionary(p => p.Id);

        return tournament.Entries
            .Where(e => byId.ContainsKey(e.ParticipantId))
            .ToDictionary(e => e.Id, e => byId[e.ParticipantId].PlayerIds);
    }

    // --- Abbildung ---------------------------------------------------------

    private sealed record DescribeContext(
        IReadOnlyDictionary<Guid, string> ParticipantNameByEntry,
        IReadOnlyDictionary<Guid, CourtAssignment> AssignmentByMatch,
        IReadOnlyDictionary<Guid, string> CourtNames);

    private async Task<DescribeContext> DescribeAsync(
        Tournament tournament,
        CancellationToken cancellationToken)
    {
        var nameByEntry = await NamesByEntryAsync(tournament, cancellationToken);
        var assignments = await _assignments.ListByTournamentAsync(tournament.Id, cancellationToken);
        var club = await _clubs.FindAsync(tournament.ClubId, cancellationToken);

        return new DescribeContext(
            nameByEntry,
            assignments
                .Where(a => !a.IsOver)
                .GroupBy(a => a.MatchId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Version).First()),
            club?.Courts.ToDictionary(c => c.Id, c => c.Name) ?? []);
    }

    private static MatchDetail Describe(Match match, DescribeContext context) => new(
        match.Id,
        match.PhaseId,
        match.Round,
        match.Position,
        match.Label,
        match.Group,
        Describe(match.Side1, context),
        Describe(match.Side2, context),
        match.Status,
        Describe(match.Score),
        Describe(context.AssignmentByMatch.GetValueOrDefault(match.Id), context),
        match.Version);

    private static MatchSideDetail Describe(MatchSide side, DescribeContext context) => new(
        side.EntryId,
        side.EntryId is { } id ? context.ParticipantNameByEntry.GetValueOrDefault(id) : null,
        side.Origin.ToString());

    private static ScoreDetail? Describe(Score? score) =>
        score is null
            ? null
            : new ScoreDetail(
                score.Outcome, score.WinnerSide, score.CompletedSets, score.AbandonedSet, score.ToString());

    private static CourtAssignmentDetail? Describe(CourtAssignment? assignment, DescribeContext context) =>
        assignment is null
            ? null
            : new CourtAssignmentDetail(
                assignment.Id,
                assignment.CourtId,
                context.CourtNames.GetValueOrDefault(assignment.CourtId, "(unbekannt)"),
                assignment.SequenceOnCourt,
                assignment.PlannedStart,
                assignment.EarliestStart,
                assignment.EstimatedDuration,
                assignment.ActualStart,
                assignment.ActualEnd,
                assignment.Source,
                assignment.Status);

    // --- Laden und Berechtigungen ------------------------------------------

    private async Task<PhaseState> BuildStateAsync(
        Tournament tournament,
        Phase phase,
        CancellationToken cancellationToken)
    {
        var definition = tournament.Format?.Definition
            ?? throw new DomainException("Das Turnier hat kein eingefrorenes Format.");

        var phaseDefinition = PhaseOrchestrator.DefinitionOf(definition, phase)
            ?? throw new DomainException($"Das Format kennt keine Phase {phase.Ordinal}.");

        return PhaseOrchestrator.StateOf(
            tournament,
            definition,
            phaseDefinition,
            phase,
            await NamesByEntryAsync(tournament, cancellationToken));
    }

    /// <summary>Der Anzeigename je Meldung.</summary>
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

    /// <summary>
    /// Lädt das Match samt <em>allen</em> Phasen seines Turniers.
    ///
    /// Alle, nicht nur die eigene: ein Ergebnis kann eine Phase abschließen und
    /// damit die Startplätze der folgenden besetzen. Läge die nicht im selben
    /// Arbeitsgang vor, bliebe der Übergang bis zum nächsten Ergebnis liegen.
    /// </summary>
    private async Task<(Tournament Tournament, IReadOnlyList<Phase> Phases, Phase Phase, Match Match)>
        LoadForResultAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var owner = await _phases.FindByMatchAsync(matchId, cancellationToken)
            ?? throw new NotFoundException("Match", matchId);

        var tournament = await LoadTournament(owner.TournamentId, cancellationToken);
        var phases = await _phases.ListByTournamentAsync(owner.TournamentId, cancellationToken);
        var phase = phases.FirstOrDefault(p => p.Id == owner.Id) ?? owner;

        return (tournament, phases, phase, phase.Matches.Single(m => m.Id == matchId));
    }

    private async Task<Tournament> LoadTournament(Guid tournamentId, CancellationToken cancellationToken) =>
        await _tournaments.FindAsync(tournamentId, cancellationToken)
        ?? throw new NotFoundException("Turnier", tournamentId);

    /// <summary>
    /// Ergebnisse darf der Schiedsrichter des Turniers ebenso eintragen wie sein
    /// Leiter oder der Administrator des ausrichtenden Vereins.
    /// </summary>
    private void RequireResultPermission(Tournament tournament) =>
        _userContext.Current.Require(
            Permission.EnterResults,
            ResourceScope.Tournament(tournament.Id),
            ResourceScope.Club(tournament.ClubId));

    /// <summary>Den Spielplan ändern darf der Schiedsrichter dagegen nicht.</summary>
    private void RequireManagePermission(Tournament tournament) =>
        _userContext.Current.Require(
            Permission.ManageTournament,
            ResourceScope.Tournament(tournament.Id),
            ResourceScope.Club(tournament.ClubId));
}
