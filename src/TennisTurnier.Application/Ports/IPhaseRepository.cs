using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Zugriff auf Phasen samt ihren Matches. Die Sichtbarkeit erben sie vom
/// Turnier (ADR-0004).
/// </summary>
public interface IPhaseRepository
{
    Task<IReadOnlyList<Phase>> ListByTournamentAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default);

    /// <summary>Die Phase, zu der dieses Match gehört — samt aller Geschwistermatches.</summary>
    Task<Phase?> FindByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);

    void Add(Phase phase);

    void RemoveRange(IEnumerable<Phase> phases);
}

/// <summary>Platzzuweisungen eines Turniers.</summary>
public interface ICourtAssignmentRepository
{
    Task<IReadOnlyList<CourtAssignment>> ListByTournamentAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default);

    Task<CourtAssignment?> FindAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Die Matches eines Turniers über alle Phasen — für die Prüfung des
    /// Spielplans, die nicht an Phasengrenzen haltmacht.
    /// </summary>
    Task<IReadOnlyList<Match>> ListMatchesAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default);

    void Add(CourtAssignment assignment);

    void Remove(CourtAssignment assignment);
}
