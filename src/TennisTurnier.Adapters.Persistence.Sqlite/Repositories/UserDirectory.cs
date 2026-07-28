using Microsoft.EntityFrameworkCore;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Repositories;

public sealed class UserDirectory : IUserDirectory
{
    private readonly TennisTurnierDbContext _db;

    public UserDirectory(TennisTurnierDbContext db) => _db = db;

    public async Task<UserAccount> EnsureAccountAsync(
        string issuer,
        string subjectId,
        string? email,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        var account = await _db.UserAccounts
            .FirstOrDefaultAsync(u => u.Issuer == issuer && u.SubjectId == subjectId, cancellationToken);

        if (account is null)
        {
            account = new UserAccount(Guid.NewGuid(), issuer, subjectId, email, displayName);
            _db.UserAccounts.Add(account);
            await _db.SaveChangesAsync(cancellationToken);
            return account;
        }

        // Nur schreiben, wenn sich wirklich etwas geändert hat — sonst erzeugt
        // jeder Request eine Schreiblast, die SQLite datenbankweit serialisiert.
        if (account.UpdateProfile(email, displayName))
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return account;
    }

    public async Task<IReadOnlyList<RoleAssignment>> GetAssignmentsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _db.RoleAssignments
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);

    public async Task AssignAsync(RoleAssignment assignment, CancellationToken cancellationToken = default)
    {
        var alreadyGranted = await _db.RoleAssignments.AnyAsync(
            a => a.UserId == assignment.UserId
                 && a.Role == assignment.Role
                 && a.Scope == assignment.Scope,
            cancellationToken);

        if (alreadyGranted)
        {
            return;
        }

        _db.RoleAssignments.Add(assignment);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await _db.RoleAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment is null)
        {
            return;
        }

        _db.RoleAssignments.Remove(assignment);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
