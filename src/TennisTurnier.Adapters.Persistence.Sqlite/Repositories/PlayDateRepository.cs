using Microsoft.EntityFrameworkCore;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Social;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Repositories;

public sealed class PlayDateRepository : IPlayDateRepository
{
    private readonly TennisTurnierDbContext _db;

    public PlayDateRepository(TennisTurnierDbContext db) => _db = db;

    /// <summary>
    /// Nächster Termin zuerst — anders als beim Feed, wo das Jüngste oben
    /// steht. Eine Verabredung ist eine Verabredung für etwas, das noch
    /// kommt; die von vorgestern interessiert niemanden mehr.
    /// </summary>
    public async Task<IReadOnlyList<PlayDate>> ListForCallerAsync(
        DateTimeOffset? from = null,
        CancellationToken cancellationToken = default) =>
        await _db.PlayDates
            .Where(date => from == null || date.StartsAt >= from)
            .OrderBy(date => date.StartsAt)
            .ToListAsync(cancellationToken);

    public Task<PlayDate?> FindAsync(Guid playDateId, CancellationToken cancellationToken = default) =>
        _db.PlayDates.FirstOrDefaultAsync(date => date.Id == playDateId, cancellationToken);

    public void Add(PlayDate playDate) => _db.PlayDates.Add(playDate);
}
