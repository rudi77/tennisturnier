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

    public Task<UserAccount?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.UserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    /// <summary>
    /// Sucht über die E-Mail-Adresse.
    ///
    /// Der Vergleich läuft über <c>EF.Functions.Like</c> ohne Platzhalter und
    /// nicht über <c>ToLower</c>: eine Funktion auf der Spalte machte den Index
    /// unbrauchbar, und LIKE ist in SQLite für ASCII ohnehin unempfindlich
    /// gegen Groß-/Kleinschreibung. Der Restvergleich im Speicher stimmt auch
    /// außerhalb von ASCII.
    /// </summary>
    public async Task<UserAccount?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _db.UserAccounts
            .AsNoTracking()
            .Where(u => u.Email != null && EF.Functions.Like(u.Email, email))
            .Take(10)
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(u =>
            string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<UserAccount>> FindManyAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default) =>
        await _db.UserAccounts
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RoleAssignment>> GetAssignmentsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _db.RoleAssignments
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Vergibt die Rolle, sofern sie nicht bereits besteht. Idempotent.
    ///
    /// Die Vorabprüfung allein genügt nicht: zwischen Lesen und Schreiben passt
    /// eine zweite Vergabe. Den Ausschlag gibt der eindeutige Index aus der
    /// Migration <c>UniqueRoleAssignment</c>; sein Verstoß bedeutet hier nichts
    /// anderes, als dass jemand anderes schneller war — und damit das gewünschte
    /// Ergebnis.
    /// </summary>
    public async Task AssignAsync(RoleAssignment assignment, CancellationToken cancellationToken = default)
    {
        if (await ExistsAsync(assignment, cancellationToken))
        {
            return;
        }

        _db.RoleAssignments.Add(assignment);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(assignment).State = EntityState.Detached;

            if (!await ExistsAsync(assignment, cancellationToken))
            {
                throw;
            }
        }
    }

    private Task<bool> ExistsAsync(RoleAssignment assignment, CancellationToken cancellationToken) =>
        _db.RoleAssignments.AnyAsync(
            a => a.UserId == assignment.UserId
                 && a.Role == assignment.Role
                 && a.Scope == assignment.Scope,
            cancellationToken);

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
