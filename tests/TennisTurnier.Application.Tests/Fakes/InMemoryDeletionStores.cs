using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.PublicView;
using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Application.Tests.Fakes;

/// <summary>
/// Die beiden Ablagen, die das Löschen eines Turniers mit aufräumen muss.
///
/// Sie stehen hier, weil die Datenbank sie nicht als Abhängigkeit kennt: eine
/// Platzzuweisung zeigt mit Restrict auf ihren Platz, und die öffentliche
/// Projektion hat überhaupt keinen Fremdschlüssel. Was der Anwendungsfall
/// vergisst, bleibt stehen — und ein Test, der es nicht sieht, deckt das nicht
/// auf.
/// </summary>
public sealed class InMemoryCourtAssignmentRepository : ICourtAssignmentRepository
{
    private readonly List<CourtAssignment> _assignments = [];

    public IReadOnlyList<CourtAssignment> All => _assignments;

    public void Seed(CourtAssignment assignment) => _assignments.Add(assignment);

    public Task<IReadOnlyList<CourtAssignment>> ListByTournamentAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CourtAssignment>>(
            [.. _assignments.Where(a => a.TournamentId == tournamentId)]);

    public Task<CourtAssignment?> FindAsync(Guid assignmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_assignments.FirstOrDefault(a => a.Id == assignmentId));

    public Task<IReadOnlyList<Match>> ListMatchesAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Match>>([]);

    public void Add(CourtAssignment assignment) => _assignments.Add(assignment);

    public void Remove(CourtAssignment assignment) => _assignments.Remove(assignment);
}

public sealed class InMemoryProjectionStore : ITournamentProjectionStore
{
    private readonly Dictionary<Guid, TournamentProjection> _projections = [];

    public IReadOnlyCollection<Guid> All => _projections.Keys;

    public void Seed(TournamentProjection projection) => _projections[projection.Id] = projection;

    public Task<TournamentProjection?> FindAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_projections.GetValueOrDefault(tournamentId));

    public void Add(TournamentProjection projection) => _projections[projection.Id] = projection;

    public void Remove(TournamentProjection projection) => _projections.Remove(projection.Id);
}
