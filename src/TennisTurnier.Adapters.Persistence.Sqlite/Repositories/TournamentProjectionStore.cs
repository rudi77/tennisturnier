using Microsoft.EntityFrameworkCore;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.PublicView;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Repositories;

/// <summary>
/// Ablage der öffentlichen Projektion.
///
/// Die einzige Ablage ohne Query-Filter, und die einzige, die das ausdrücklich
/// sein soll: sie wird von Zuschauern ohne Anmeldung gelesen (ADR-0003). Der
/// Schutz liegt vollständig darin, was
/// <see cref="TennisTurnier.Application.PublicView.TournamentViewBuilder"/> in
/// die Projektion schreibt.
/// </summary>
public sealed class TournamentProjectionStore : ITournamentProjectionStore
{
    private readonly TennisTurnierDbContext _db;

    public TournamentProjectionStore(TennisTurnierDbContext db) => _db = db;

    public async Task<TournamentProjection?> FindAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        await _db.TournamentProjections.FirstOrDefaultAsync(p => p.Id == tournamentId, cancellationToken);

    public void Add(TournamentProjection projection) => _db.TournamentProjections.Add(projection);

    public void Remove(TournamentProjection projection) => _db.TournamentProjections.Remove(projection);
}
