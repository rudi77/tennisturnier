using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Application.Social;
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
    private readonly IPlayerRepository _players;
    private readonly IPublicViewService _publicView;
    private readonly FeedRecorder _feed;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public MatchService(
        ITournamentRepository tournaments,
        IPhaseRepository phases,
        ICourtAssignmentRepository assignments,
        IPlayerRepository players,
        IPublicViewService publicView,
        FeedRecorder feed,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _tournaments = tournaments;
        _phases = phases;
        _assignments = assignments;
        _players = players;
        _publicView = publicView;
        _feed = feed;
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
        // und keine Eingabe. Sie wird deshalb auch als das ausgeführt, was sie
        // ist: erst zurücknehmen, dann neu eintragen.
        //
        // Das ist kein Umweg, sondern der Unterschied zwischen einer Korrektur,
        // die durchschlägt, und einer, die nur die eine Zeile ändert. Das
        // Zurücknehmen prüft, ob ein Folgematch schon entschieden ist, und lässt
        // die Runden verwerfen, die aus dem alten Ergebnis hervorgegangen sind —
        // beim Überschreiben bliebe die Runde „vollständig", und das Schweizer
        // System spielte mit Paarungen weiter, die zur neuen Tabelle nicht mehr
        // passen.
        if (match.Score is not null)
        {
            RequireLaterPhasesUntouched(tournament, phases, phase);

            phase.ClearResult(matchId);
            await AdvancePhasesAsync(tournament, phases, cancellationToken);
        }

        var matchFormat = MatchFormatOf(tournament, phase);
        phase.RecordResult(matchId, BuildScore(request, match, matchFormat));

        await AnnounceResultAsync(tournament, phase, match, cancellationToken);

        // Das erste Ergebnis macht aus einem ausgelosten ein laufendes Turnier.
        if (tournament.State == TournamentState.DrawGenerated)
        {
            var vorher = tournament.State;
            tournament.Start();
            _feed.RecordStateChange(tournament, vorher);
        }

        await ReleaseQueueAsync(tournament.Id, [matchId], cancellationToken);
        await AdvancePhasesAsync(tournament, phases, cancellationToken);

        // Zwischenspeichern, bevor die öffentliche Ansicht entsteht: sie soll den
        // Stand der Datenbank abbilden und nicht die Kopien vom Anfang des
        // Requests, in denen die Ergebnisse paralleler Eingaben fehlen.
        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournament.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Schreibt das Ergebnis in den Feed (ADR-0014).
    ///
    /// Nach dem Eintragen und nicht davor: erst dann steht fest, wer gewonnen
    /// hat. Ein Freilos bleibt außen vor — es wurde nie gespielt, und eine
    /// Zeile darüber wäre eine Meldung über etwas, das nicht stattgefunden hat.
    /// </summary>
    private async Task AnnounceResultAsync(
        Tournament tournament,
        Phase phase,
        Match match,
        CancellationToken cancellationToken)
    {
        if (match.Score is not { } score || score.Outcome == MatchOutcome.Bye)
        {
            return;
        }

        var names = await NamesByEntryAsync(tournament, cancellationToken);

        var winner = match.Side(score.WinnerSide).EntryId;
        var loser = match.Side(score.LoserSide).EntryId;

        if (winner is null || loser is null)
        {
            return;
        }

        _feed.RecordResult(
            tournament.Id,
            match.Id,
            match.Label ?? $"{phase.Name}, Runde {match.Round}",
            names.GetValueOrDefault(winner.Value, "(unbekannt)"),
            names.GetValueOrDefault(loser.Value, "(unbekannt)"),
            score);
    }

    /// <summary>
    /// Gibt den Platz frei, den ein entschiedenes Match noch belegt.
    ///
    /// Zwei Fälle, und sie enden verschieden:
    ///
    ///  - Eine <em>wartende</em> Zuweisung fällt weg. Nicht jedes Match wird am
    ///    Platz aufgerufen: ein Nichtantreten wird eingetragen, ohne dass jemand
    ///    hingeht. Bliebe sie stehen, behielte sie ihre Nummer in der
    ///    Warteschlange, blockierte anderthalb Stunden für alles dahinter und
    ///    stünde öffentlich als wartendes Match.
    ///  - Eine <em>lebende</em> Zuweisung — aufgerufen, laufend, unterbrochen —
    ///    wird abgeschlossen und bleibt als Historie stehen. Sie zu löschen
    ///    hieße zu behaupten, auf diesem Platz sei nie gespielt worden
    ///    (ADR-0002).
    ///
    /// Sie stehen zu lassen wäre beides nicht. Wenn ein Ergebnis eingetragen
    /// ist, ist das Match vorbei — die Spieler stehen dann nicht mehr am Platz.
    /// Vorher blieb die Zuweisung auf „läuft": das Turnier zeigte nach seinem
    /// letzten Ergebnis eine laufende Partie, und schlimmer, der Platz galt als
    /// belegt und ließ sich für das nächste Match nicht mehr aufrufen. Beenden
    /// von Hand über „Platz frei" gibt es weiterhin — es ist der übliche Weg,
    /// weil der Platz frei ist, sobald die Spieler ihn verlassen, und nicht
    /// erst, wenn jemand Zeit hatte, den Zettel auszufüllen. Es ist nur nicht
    /// mehr der einzige.
    /// </summary>
    private async Task ReleaseQueueAsync(
        Guid tournamentId,
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken)
    {
        if (matchIds.Count == 0)
        {
            return;
        }

        var all = await _assignments.ListByTournamentAsync(tournamentId, cancellationToken);
        var affected = all.Where(a => matchIds.Contains(a.MatchId)).ToList();

        var waiting = affected.Where(a => a.Status == AssignmentStatus.Planned).ToList();

        foreach (var assignment in waiting)
        {
            _assignments.Remove(assignment);
        }

        var live = affected
            .Where(a => a.Status is AssignmentStatus.Called or AssignmentStatus.Running
                or AssignmentStatus.Suspended)
            .ToList();

        foreach (var assignment in live)
        {
            assignment.Finish(_clock.Now);
        }

        // Die betroffenen Plätze rücken nach: sonst bliebe eine Lücke in der
        // Nummerierung, und die wird am Platz vorgelesen. Ein eben abgeschlossener
        // Platz ist dabei der wichtigere der beiden Fälle — dort wartet jemand.
        foreach (var courtId in waiting.Concat(live).Select(a => a.CourtId).Distinct())
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
        // Ein Turnier, dessen Ergebnis zurückgenommen wird, hat Phasen — und
        // damit ein eingefrorenes Format.
        var definition = tournament.Format!.Definition;

        var dependent = phases.FirstOrDefault(other =>
            other.Id != phase.Id
            && other.HasAnyResult
            && PhaseOrchestrator.DefinitionOf(definition, other)!.Qualification?.FromPhase == phase.Ordinal);

        if (dependent is not null)
        {
            throw new DomainException(
                $"Das Ergebnis lässt sich nicht ändern, weil in „{dependent.Name}“ bereits gespielt wurde. " +
                "Zuerst die Ergebnisse dieser Phase zurücknehmen.");
        }
    }

    /// <summary>
    /// Reicht abgeschlossene Phasen an die jeweils folgende weiter und setzt an,
    /// was anzusetzen ist (ADR-0001).
    ///
    /// Was der Turniertag schon in der Hand hat, bleibt dabei unangetastet: ein
    /// Match, das aufgerufen wurde, läuft oder unterbrochen ist, wird nicht
    /// zurückgenommen, auch wenn seine Paarung hinfällig geworden ist. Für die
    /// übrigen wird der Platz freigegeben und die Warteschlange nachgezogen —
    /// eine Ansetzung ohne Match wäre eine Nummer, die am Platz vorgelesen wird
    /// und zu der niemand kommt.
    /// </summary>
    private async Task AdvancePhasesAsync(
        Tournament tournament,
        IReadOnlyList<Phase> phases,
        CancellationToken cancellationToken)
    {
        var assignments = await _assignments.ListByTournamentAsync(tournament.Id, cancellationToken);

        var onCourt = assignments
            .Where(a => a.Status is AssignmentStatus.Called or AssignmentStatus.Running
                or AssignmentStatus.Suspended)
            .Select(a => a.MatchId)
            .ToHashSet();

        var names = await NamesByEntryAsync(tournament, cancellationToken);

        var withdrawn = PhaseOrchestrator.Advance(tournament, phases, names, onCourt);

        await ReleaseQueueAsync(tournament.Id, withdrawn, cancellationToken);

        SyncCompletion(tournament, phases, names);
    }

    /// <summary>
    /// Schließt das Turnier ab, wenn nichts mehr zu spielen ist — und nimmt den
    /// Abschluss zurück, wenn wieder etwas zu spielen ist.
    ///
    /// Das folgt aus dem Ergebnis und ist keine eigene Handlung: das erste
    /// Ergebnis macht aus einem ausgelosten ein laufendes Turnier, das letzte
    /// aus einem laufenden ein abgeschlossenes. Es einer Schaltfläche zu
    /// überlassen hieße, dass ein Turnier so lange „läuft", bis jemand daran
    /// denkt — und der Endpunkt <c>complete</c> existiert für den Fall, dass
    /// abgebrochen wird, bevor alles gespielt ist, nicht für den Normalfall.
    ///
    /// Beide Richtungen an einer Stelle, weil sie dieselbe Frage beantworten.
    /// Nur die eine zu haben wäre die schlechtere Hälfte: das Finale wäre dann
    /// das einzige Match, dessen Ergebnis sich nicht mehr korrigieren ließe.
    /// </summary>
    private static void SyncCompletion(
        Tournament tournament,
        IReadOnlyList<Phase> phases,
        IReadOnlyDictionary<Guid, string> namesByEntry)
    {
        var finished = PhaseOrchestrator.IsFinished(tournament, phases, namesByEntry);

        if (finished)
        {
            // Schon abgeschlossen heißt: nichts zu tun. Das ist der Weg jeder
            // weiteren Korrektur an einem fertigen Turnier.
            if (tournament.State == TournamentState.InProgress)
            {
                tournament.Complete();
            }

            return;
        }

        if (tournament.State == TournamentState.Completed)
        {
            tournament.Resume();
        }
    }

    public async Task<AssignCourtResult> AssignCourtAsync(
        Guid matchId,
        AssignCourtRequest request,
        CancellationToken cancellationToken = default)
    {
        // Das Match wird über die Phase geladen: es muss existieren und zu einem
        // sichtbaren Turnier gehören, bevor ihm ein Platz zugewiesen wird.
        var (tournament, _, _, _) = await LoadForResultAsync(matchId, cancellationToken);
        RequireManagePermission(tournament);

        var court = tournament.Courts.FirstOrDefault(c => c.Id == request.CourtId)
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

    /// <summary>
    /// Das Satzformat, unter dem dieses Match gespielt wird.
    ///
    /// Ein Match gibt es nur in einer Phase, und Phasen entstehen beim Auslosen
    /// aus dem eingefrorenen Format. Beides ist hier also da.
    /// </summary>
    private static MatchFormat MatchFormatOf(Tournament tournament, Phase phase)
    {
        var definition = tournament.Format!.Definition;

        return definition.MatchFormatOf(definition.Phases.First(p => p.Ordinal == phase.Ordinal));
    }

    // --- Spielplanprüfung --------------------------------------------------

    private async Task<IReadOnlyList<ScheduleViolationDetail>> ValidateAsync(
        Tournament tournament,
        IReadOnlyList<CourtAssignment> assignments,
        CancellationToken cancellationToken)
    {
        var matches = await _assignments.ListMatchesAsync(tournament.Id, cancellationToken);
        var playersByEntry = await PlayersByEntryAsync(tournament, cancellationToken);

        var range = tournament.Period();

        var windows = tournament.Courts.ToDictionary(
            court => court.Id,
            court => court.FreeWindows(range));

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
        IReadOnlyDictionary<Guid, string> CourtNames,
        IReadOnlyDictionary<Guid, string> MatchLabels);

    private async Task<DescribeContext> DescribeAsync(
        Tournament tournament,
        CancellationToken cancellationToken)
    {
        var nameByEntry = await NamesByEntryAsync(tournament, cancellationToken);
        var assignments = await _assignments.ListByTournamentAsync(tournament.Id, cancellationToken);

        // Über alle Phasen hinweg: eine Herkunft darf auf ein Match einer
        // früheren Phase zeigen — der Qualifikant kommt aus der Gruppenphase.
        var phases = await _phases.ListByTournamentAsync(tournament.Id, cancellationToken);
        var matches = phases.SelectMany(phase => phase.Matches).ToList();

        return new DescribeContext(
            nameByEntry,
            assignments
                .Where(a => !a.IsOver)
                .GroupBy(a => a.MatchId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Version).First()),
            tournament.Courts.ToDictionary(c => c.Id, c => c.Name),
            MatchOrigins.LabelsOf(matches));
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
        MatchOrigins.Describe(side.Origin, context.MatchLabels));

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
        // Wie in MatchFormatOf: eine Phase gibt es nur zu einem eingefrorenen
        // Format, und ihre Definition steht darin.
        var definition = tournament.Format!.Definition;
        var phaseDefinition = PhaseOrchestrator.DefinitionOf(definition, phase)!;

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

    /// <summary>
    /// Das Turnier zu einer Phase. Es ist da — die Phase gehört ihm, und der
    /// Query-Filter zeigt beide oder keines von beiden (ADR-0004).
    /// </summary>
    private async Task<Tournament> LoadTournament(Guid tournamentId, CancellationToken cancellationToken) =>
        (await _tournaments.FindAsync(tournamentId, cancellationToken))!;

    /// <summary>
    /// Ergebnisse darf der Schiedsrichter des Turniers ebenso eintragen wie sein
    /// Leiter oder der Administrator des ausrichtenden Vereins.
    /// </summary>
    private void RequireResultPermission(Tournament tournament) =>
        _userContext.Current.Require(
            Permission.EnterResults,
            ResourceScope.Tournament(tournament.Id));

    /// <summary>Den Spielplan ändern darf der Schiedsrichter dagegen nicht.</summary>
    private void RequireManagePermission(Tournament tournament) =>
        _userContext.Current.Require(
            Permission.ManageTournament,
            ResourceScope.Tournament(tournament.Id));
}
