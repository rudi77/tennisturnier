using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Tests.Fakes;

/// <summary>
/// Merkt sich die vergebenen Rollen. Damit lässt sich nicht nur prüfen,
/// <em>dass</em> etwas vergeben wurde, sondern auch, dass nichts vergeben wurde,
/// wo es nicht hingehört — das ist bei einer Berechtigungsentscheidung der
/// wichtigere der beiden Fälle.
/// </summary>
public sealed class RecordingUserDirectory : IUserDirectory
{
    private readonly List<RoleAssignment> _assigned = [];

    public IReadOnlyList<RoleAssignment> Assigned => _assigned;

    /// <summary>Die Konten, die dieser Verzeichnisdienst kennt — vom Test gesetzt.</summary>
    public List<UserAccount> Accounts { get; } = [];

    public UserAccount Seed(string email, string displayName)
    {
        var account = new UserAccount(
            Guid.NewGuid(), "https://test.local", $"sub-{Guid.NewGuid():N}", email, displayName);

        Accounts.Add(account);
        return account;
    }

    public Task<UserAccount> EnsureAccountAsync(
        string issuer,
        string subjectId,
        string? email,
        string? displayName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new UserAccount(Guid.NewGuid(), issuer, subjectId, email, displayName));

    public Task<UserAccount?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Accounts.FirstOrDefault(a => a.Id == userId));

    public Task<UserAccount?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Accounts.FirstOrDefault(a =>
            a.Email is { } stored && string.Equals(stored, email, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<UserAccount>> FindManyAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserAccount>>(
            [.. Accounts.Where(a => userIds.Contains(a.Id))]);

    public Task<IReadOnlyList<RoleAssignment>> GetAssignmentsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RoleAssignment>>(
            _assigned.Where(a => a.UserId == userId).ToList());

    public Task AssignAsync(RoleAssignment assignment, CancellationToken cancellationToken = default)
    {
        _assigned.Add(assignment);
        return Task.CompletedTask;
    }

    public Task RevokeAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        _assigned.RemoveAll(a => a.Id == assignmentId);
        return Task.CompletedTask;
    }
}
