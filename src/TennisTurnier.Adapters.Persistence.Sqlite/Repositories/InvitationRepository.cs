using Microsoft.EntityFrameworkCore;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Repositories;

/// <summary>
/// Einladungen innerhalb der laufenden Arbeitseinheit — wie die
/// Rollenzuweisungen und aus demselben Grund.
/// </summary>
public sealed class InvitationRepository : IInvitationRepository
{
    private readonly TennisTurnierDbContext _db;

    public InvitationRepository(TennisTurnierDbContext db) => _db = db;

    public void Add(Invitation invitation) => _db.Invitations.Add(invitation);

    public void Remove(Invitation invitation) => _db.Invitations.Remove(invitation);

    public async Task<IReadOnlyList<Invitation>> ListByTournamentAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        await _db.Invitations
            .AsNoTracking()
            .Where(i => i.TournamentId == tournamentId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Verglichen wird gegen die kleingeschriebene Adresse, weil genau die
    /// gespeichert wird. Ohne <c>ToLower</c> auf der Spalte bleibt der Index
    /// brauchbar — die Domäne hat die Normalisierung schon erledigt, hier ist
    /// nur der Suchbegriff nachzuziehen.
    /// </summary>
    public async Task<IReadOnlyList<Invitation>> ListByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalisiert = email.Trim().ToLowerInvariant();

        return await _db.Invitations
            .AsNoTracking()
            .Where(i => i.Email == normalisiert)
            .ToListAsync(cancellationToken);
    }
}
