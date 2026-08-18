using Microsoft.EntityFrameworkCore;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Repositories;

/// <summary>
/// Rollenzuweisungen innerhalb der laufenden Arbeitseinheit — anders als
/// <see cref="UserDirectory"/>, das selbst speichert.
/// </summary>
public sealed class RoleAssignmentRepository : IRoleAssignmentRepository
{
    private readonly TennisTurnierDbContext _db;

    public RoleAssignmentRepository(TennisTurnierDbContext db) => _db = db;

    public void Add(RoleAssignment assignment) => _db.RoleAssignments.Add(assignment);

    public void Remove(RoleAssignment assignment) => _db.RoleAssignments.Remove(assignment);

    /// <summary>
    /// Wer welche Rolle an diesem Turnier hat.
    ///
    /// Auf <c>RoleAssignments</c> liegt kein Query-Filter — die Tabelle ist die
    /// Grundlage des Filters und könnte nicht von ihm abhängen, ohne sich
    /// selbst im Kreis zu drehen. Die Berechtigung prüft deshalb der
    /// Anwendungsfall, bevor er hier hereinkommt.
    /// </summary>
    public async Task<IReadOnlyList<RoleAssignment>> ListByTournamentAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        await _db.RoleAssignments
            .AsNoTracking()
            .Where(a => a.Scope.ResourceId == tournamentId)
            .ToListAsync(cancellationToken);
}
