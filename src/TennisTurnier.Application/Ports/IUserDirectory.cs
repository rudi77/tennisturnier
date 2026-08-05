using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Benutzerkonten und ihre Rollenzuweisungen.
///
/// Der Identity Provider liefert nur Identität; welche Rollen jemand hat, weiß
/// ausschließlich diese Anwendung (ADR-0007).
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// Findet das lokale Konto zum Token oder legt es beim ersten Login an.
    /// </summary>
    Task<UserAccount> EnsureAccountAsync(
        string issuer,
        string subjectId,
        string? email,
        string? displayName,
        CancellationToken cancellationToken = default);

    /// <summary>Das lokale Konto zur Benutzerkennung, oder <c>null</c>.</summary>
    Task<UserAccount?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleAssignment>> GetAssignmentsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task AssignAsync(RoleAssignment assignment, CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid assignmentId, CancellationToken cancellationToken = default);
}
