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
    /// Löscht unmittelbar und ohne Erwartung an die Zeilenzahl.
    ///
    /// <c>ExecuteDelete</c> statt der Änderungsverfolgung: die prüft beim
    /// Löschen, ob genau eine Zeile getroffen wurde, und meldet sonst einen
    /// Nebenläufigkeitskonflikt. Beim ersten Login ist „schon gelöscht" aber
    /// der Normalfall — die Oberfläche stellt mehrere Anfragen zugleich, und
    /// jede läuft durch die Benutzerauflösung.
    ///
    /// Läuft in der offenen Transaktion der Arbeitseinheit mit, sofern eine
    /// besteht: EF führt den Befehl auf derselben Verbindung aus.
    /// </summary>
    public Task RemoveRedeemedAsync(
        IReadOnlyCollection<Guid> invitationIds,
        CancellationToken cancellationToken = default) =>
        _db.Invitations
            .Where(i => invitationIds.Contains(i.Id))
            .ExecuteDeleteAsync(cancellationToken);

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
