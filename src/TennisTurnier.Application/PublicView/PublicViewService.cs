using System.Text.Json;
using System.Text.Json.Serialization;
using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.PublicView;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.PublicView;

/// <summary>Ein ausgelieferter Stand der öffentlichen Ansicht.</summary>
public sealed record PublicTournamentSnapshot(string Json, string ETag, DateTimeOffset UpdatedAt, int Version);

/// <summary>Die öffentliche Ansicht: lesen und neu aufbauen (ADR-0003).</summary>
public interface IPublicViewService
{
    /// <summary>
    /// Der aktuelle Stand, oder <c>null</c>, solange es nichts zu zeigen gibt.
    /// Ohne Rechteprüfung — das ist der Sinn der Sache.
    /// </summary>
    Task<PublicTournamentSnapshot?> GetAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Baut die Ansicht neu und gibt zurück, ob sich inhaltlich etwas geändert
    /// hat. Nur dann lohnt ein Push.
    ///
    /// Speichert ausdrücklich nicht: die neue Ansicht gehört in dieselbe Einheit
    /// der Arbeit wie die Änderung, aus der sie folgt. Ein zweites Speichern
    /// hinterher läse einen Stand, den parallele Anfragen inzwischen überholt
    /// haben, schriebe ihn als letzter fest — und meldete dem Aufrufer obendrein
    /// einen Konflikt für etwas, das längst gespeichert ist.
    ///
    /// Ohne Rechteprüfung: die Anwendungsfälle rufen das im Anschluss an eine
    /// bereits geprüfte Handlung, und ein Schiedsrichter darf ein Ergebnis
    /// eintragen, ohne den Spielplan verwalten zu dürfen.
    /// </summary>
    Task<bool> RebuildAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Neuaufbau auf ausdrückliche Anweisung der Turnierleitung.
    ///
    /// Die Projektion ist abgeleiteter Zustand. Ohne diesen Weg wäre ein Fehler
    /// im Bauplan nach seiner Behebung nur durch Datenreparatur aus der Welt zu
    /// schaffen — die alten Stände blieben stehen, bis zufällig jemand ein
    /// Ergebnis einträgt (ADR-0003).
    /// </summary>
    Task RebuildOnDemandAsync(Guid tournamentId, CancellationToken cancellationToken = default);
}

public sealed class PublicViewService : IPublicViewService
{
    /// <summary>
    /// Aufzählungen als Text, anders als in der internen API.
    ///
    /// Diese Antwort lesen fremde Clients, die nichts von unseren Typen wissen.
    /// Eine 3 in einem Statusfeld ist für sie bedeutungslos und bricht still,
    /// sobald jemand einen Wert dazwischenschiebt.
    /// </summary>
    private static readonly JsonSerializerOptions PublicJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ITournamentRepository _tournaments;
    private readonly IClubRepository _clubs;
    private readonly IPhaseRepository _phases;
    private readonly ICourtAssignmentRepository _assignments;
    private readonly IPlayerRepository _players;
    private readonly ITournamentProjectionStore _projections;
    private readonly ITournamentNotifier _notifier;
    private readonly IPostCommitQueue _postCommit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public PublicViewService(
        ITournamentRepository tournaments,
        IClubRepository clubs,
        IPhaseRepository phases,
        ICourtAssignmentRepository assignments,
        IPlayerRepository players,
        ITournamentProjectionStore projections,
        ITournamentNotifier notifier,
        IPostCommitQueue postCommit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _tournaments = tournaments;
        _clubs = clubs;
        _phases = phases;
        _assignments = assignments;
        _players = players;
        _projections = projections;
        _notifier = notifier;
        _postCommit = postCommit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<PublicTournamentSnapshot?> GetAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default)
    {
        var projection = await _projections.FindAsync(tournamentId, cancellationToken);

        return projection is null
            ? null
            : new PublicTournamentSnapshot(
                projection.Json, projection.ETag, projection.UpdatedAt, projection.Version);
    }

    public async Task RebuildOnDemandAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        // Der einzige Aufruf ohne umgebenden Anwendungsfall — er speichert daher
        // selbst.
        // Erst laden, dann prüfen: ein Turnier außerhalb des Scopes soll als 404
        // enden und nicht als 403 (ADR-0004).
        var tournament = await _tournaments.FindAsync(tournamentId, cancellationToken)
            ?? throw new NotFoundException("Turnier", tournamentId);

        _userContext.Current.Require(
            Permission.ManageTournament,
            ResourceScope.Tournament(tournament.Id));

        if (await RebuildAsync(tournamentId, cancellationToken))
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> RebuildAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await _tournaments.FindAsync(tournamentId, cancellationToken)
            ?? throw new NotFoundException("Turnier", tournamentId);

        var existing = await _projections.FindAsync(tournamentId, cancellationToken);

        // Vor der Auslosung gibt es kein Bracket, keine Tabelle und keinen
        // Spielplan — nur eine Meldeliste, und die ist nicht öffentlich. Wird ein
        // Draw zurückgenommen, verschwindet die Ansicht wieder: eine
        // stehengebliebene Auslosung wäre schlimmer als keine.
        if (tournament.Format is null)
        {
            if (existing is null)
            {
                return false;
            }

            _projections.Remove(existing);
            Announce(tournamentId, etag: string.Empty);

            return true;
        }

        var json = JsonSerializer.Serialize(await BuildAsync(tournament, cancellationToken), PublicJson);
        var projection = existing;

        if (projection is null)
        {
            projection = new TournamentProjection(tournamentId, json, _clock.Now);
            _projections.Add(projection);
        }
        else if (!projection.Replace(json, _clock.Now))
        {
            return false;
        }

        Announce(tournamentId, projection.ETag);

        return true;
    }

    /// <summary>
    /// Meldet den neuen Stand — aber erst, wenn gespeichert ist. Ginge der Push
    /// vorher hinaus, holten sich alle Zuschauer einen Stand, den es nach einem
    /// Rollback nie gegeben hat.
    /// </summary>
    private void Announce(Guid tournamentId, string etag) =>
        _postCommit.Enqueue(ct => _notifier.ProjectionChangedAsync(tournamentId, etag, ct));

    private async Task<PublicTournamentView> BuildAsync(
        Tournament tournament,
        CancellationToken cancellationToken)
    {
        var club = await _clubs.FindAsync(tournament.ClubId, cancellationToken)
            ?? throw new NotFoundException("Verein", tournament.ClubId);

        var phases = await _phases.ListByTournamentAsync(tournament.Id, cancellationToken);
        var assignments = await _assignments.ListByTournamentAsync(tournament.Id, cancellationToken);
        var names = await NamesByEntryAsync(tournament, cancellationToken);

        var definition = tournament.Format!.Definition;
        var standings = new Dictionary<Guid, Standings>();

        foreach (var phase in phases)
        {
            if (PhaseOrchestrator.DefinitionOf(definition, phase) is not { } phaseDefinition)
            {
                continue;
            }

            var state = PhaseOrchestrator.StateOf(tournament, definition, phaseDefinition, phase, names);

            standings[phase.Id] = PhaseFormats.For(phase.Format).ComputeStandings(state);
        }

        return TournamentViewBuilder.Build(tournament, club, phases, standings, names, assignments);
    }

    /// <summary>
    /// Der Anzeigename je Meldung — und ausdrücklich nur er. Der Spieler dahinter
    /// trägt Kontaktdaten und ein Geburtsdatum, die hier nicht einmal geladen
    /// werden sollen, um nicht versehentlich weitergereicht zu werden.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> NamesByEntryAsync(
        Tournament tournament,
        CancellationToken cancellationToken)
    {
        var participantIds = tournament.Entries.Select(e => e.ParticipantId).Distinct().ToList();
        var participants = await _players.FindParticipantsAsync(participantIds, cancellationToken);
        var byId = participants.ToDictionary(p => p.Id, p => p.DisplayName);

        return tournament.Entries.ToDictionary(
            entry => entry.Id,
            entry => byId.GetValueOrDefault(entry.ParticipantId, "(unbekannt)"));
    }
}
