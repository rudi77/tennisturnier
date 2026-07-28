using Microsoft.EntityFrameworkCore;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Clubs;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Repositories;

public sealed class ClubRepository : IClubRepository
{
    private readonly TennisTurnierDbContext _db;

    public ClubRepository(TennisTurnierDbContext db) => _db = db;

    /// <summary>
    /// Plätze, Öffnungszeiten und Sperren hängen per <c>AutoInclude</c> am Verein.
    ///
    /// Sperren wachsen über die Jahre; bei einem Verein mit einem Dutzend Plätzen
    /// bleibt das über Jahre im dreistelligen Bereich und damit unkritisch. Sobald
    /// das nicht mehr stimmt, gehört hier ein Zeitraumfilter hin — nicht später
    /// ein Cache.
    /// </summary>
    public Task<Club?> FindAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        _db.Clubs.FirstOrDefaultAsync(c => c.Id == clubId, cancellationToken);

    public async Task<IReadOnlyList<Club>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.Clubs
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

    public void Add(Club club) => _db.Clubs.Add(club);

    public void Remove(Club club) => _db.Clubs.Remove(club);
}
