using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Tests.Fakes;

/// <summary>
/// Rollenzuweisungen im Arbeitsspeicher.
///
/// Wie das echte Repository speichert es nicht selbst — das ist der ganze Punkt
/// dieses Ports. Ein Fake, der es täte, würde melden, dass die Zuweisung
/// überlebt hat, auch wenn der Speichervorgang des Turniers scheiterte.
/// </summary>
public sealed class InMemoryRoleAssignmentRepository : IRoleAssignmentRepository
{
    private readonly List<RoleAssignment> _assignments = [];

    public IReadOnlyList<RoleAssignment> Assignments => _assignments;

    public void Add(RoleAssignment assignment) => _assignments.Add(assignment);

    public void Remove(RoleAssignment assignment) => _assignments.Remove(assignment);

    public Task<IReadOnlyList<RoleAssignment>> ListByTournamentAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RoleAssignment>>(
            [.. _assignments.Where(a => a.Scope.ResourceId == tournamentId)]);
}
