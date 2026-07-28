using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Tests.Fakes;

/// <summary>
/// Vereinsspeicher im Arbeitsspeicher, der den Club-Scope genauso anwendet wie
/// der echte Query-Filter (ADR-0004).
///
/// Diese Nachbildung ist der Punkt, an dem die Anwendungstests ehrlich bleiben:
/// Ein Fake ohne Scope-Filter würde melden, dass ein Anwendungsfall funktioniert,
/// obwohl er auf der echten Datenbank nichts zu sehen bekäme.
/// </summary>
public sealed class InMemoryClubRepository : IClubRepository
{
    private readonly Dictionary<Guid, Club> _clubs = [];
    private readonly IUserContext _userContext;

    public InMemoryClubRepository(IUserContext userContext) => _userContext = userContext;

    /// <summary>Legt einen Verein an, ohne den Scope zu prüfen — für den Testaufbau.</summary>
    public Club Seed(Club club)
    {
        _clubs[club.Id] = club;
        return club;
    }

    public int SavedChanges { get; private set; }

    public Task<Club?> FindAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Visible().FirstOrDefault(c => c.Id == clubId));

    public Task<IReadOnlyList<Club>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Club>>(Visible().OrderBy(c => c.Name).ToList());

    public void Add(Club club) => _clubs[club.Id] = club;

    public void Remove(Club club) => _clubs.Remove(club.Id);

    internal void RecordSave() => SavedChanges++;

    private IEnumerable<Club> Visible()
    {
        var user = _userContext.Current;

        return user.IsSystemAdmin
            ? _clubs.Values
            : _clubs.Values.Where(c => user.ClubIds.Contains(c.Id));
    }
}

public sealed class MutableUserContext : IUserContext
{
    public UserPrincipal Current { get; set; } = UserPrincipal.Anonymous;
}

public sealed class RecordingUnitOfWork : IUnitOfWork
{
    private readonly InMemoryClubRepository _clubs;

    public RecordingUnitOfWork(InMemoryClubRepository clubs) => _clubs = clubs;

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        _clubs.RecordSave();
        return Task.CompletedTask;
    }
}
