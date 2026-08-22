using Microsoft.EntityFrameworkCore;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Repositories;

public sealed class PhaseRepository : IPhaseRepository
{
    private readonly TennisTurnierDbContext _db;

    public PhaseRepository(TennisTurnierDbContext db) => _db = db;

    public async Task<IReadOnlyList<Phase>> ListByTournamentAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        await _db.Phases
            .Where(p => p.TournamentId == tournamentId)
            .OrderBy(p => p.Ordinal)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Lädt die Phase über eines ihrer Matches — samt aller Geschwister.
    ///
    /// Das ist Absicht: ein Ergebnis wird an die Folgematches weitergereicht,
    /// und dafür muss die ganze Phase im Speicher sein. Nur das eine Match zu
    /// laden hieße, die Propagation zu verlieren.
    /// </summary>
    public async Task<Phase?> FindByMatchAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        // In einem Zug statt in zweien: erst die Phasen-Id holen und dann die
        // Phase hieße, für ein unbekanntes Match zweimal zu fragen.
        return await _db.Phases.FirstOrDefaultAsync(
            phase => _db.Matches.Any(m => m.Id == matchId && m.PhaseId == phase.Id),
            cancellationToken);
    }

    public void Add(Phase phase) => _db.Phases.Add(phase);

    public void RemoveRange(IEnumerable<Phase> phases) => _db.Phases.RemoveRange(phases);
}

public sealed class CourtAssignmentRepository : ICourtAssignmentRepository
{
    private readonly TennisTurnierDbContext _db;

    public CourtAssignmentRepository(TennisTurnierDbContext db) => _db = db;

    public async Task<IReadOnlyList<CourtAssignment>> ListByTournamentAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        await _db.CourtAssignments
            .Where(a => a.TournamentId == tournamentId)
            .OrderBy(a => a.CourtId)
            .ThenBy(a => a.SequenceOnCourt)
            .ToListAsync(cancellationToken);

    public Task<CourtAssignment?> FindAsync(Guid assignmentId, CancellationToken cancellationToken = default) =>
        _db.CourtAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);

    public async Task<IReadOnlyList<Match>> ListMatchesAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        await _db.Matches
            .Where(m => m.TournamentId == tournamentId)
            .ToListAsync(cancellationToken);

    public void Add(CourtAssignment assignment) => _db.CourtAssignments.Add(assignment);

    public void Remove(CourtAssignment assignment) => _db.CourtAssignments.Remove(assignment);
}
