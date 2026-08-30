using Microsoft.EntityFrameworkCore;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Social;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Repositories;

public sealed class FeedRepository : IFeedRepository
{
    private readonly TennisTurnierDbContext _db;

    public FeedRepository(TennisTurnierDbContext db) => _db = db;

    /// <summary>
    /// Jüngste zuerst — so wird der Feed gelesen, und so lässt er sich mit
    /// einem Zeitstempel weiterblättern, ohne dass ein eingeschobener Eintrag
    /// die Seitengrenzen verschiebt.
    ///
    /// Ohne <c>AsNoTracking</c>: der Aufrufer kommentiert und löscht auf
    /// derselben Menge, und ein nachverfolgter Eintrag erspart ihm das zweite
    /// Laden.
    /// </summary>
    public async Task<IReadOnlyList<TournamentPost>> ListAsync(
        Guid tournamentId,
        int limit,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default) =>
        await _db.TournamentPosts
            .Where(post => post.TournamentId == tournamentId
                           && (before == null || post.CreatedAt < before))
            .OrderByDescending(post => post.CreatedAt)
            .ThenByDescending(post => post.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<TournamentPost?> FindAsync(Guid postId, CancellationToken cancellationToken = default) =>
        _db.TournamentPosts.FirstOrDefaultAsync(post => post.Id == postId, cancellationToken);

    public void Add(TournamentPost post) => _db.TournamentPosts.Add(post);

    public void Remove(TournamentPost post) => _db.TournamentPosts.Remove(post);
}
