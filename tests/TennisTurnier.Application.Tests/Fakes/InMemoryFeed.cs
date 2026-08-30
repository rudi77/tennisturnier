using TennisTurnier.Application.Ports;
using TennisTurnier.Application.Social;
using TennisTurnier.Domain.Social;

namespace TennisTurnier.Application.Tests.Fakes;

/// <summary>
/// Der Feed im Speicher (ADR-0014).
///
/// Er steht hier, weil <see cref="FeedRecorder"/> ein gewöhnlicher Mitarbeiter
/// der Anwendungsdienste ist und keine Schnittstelle davor hat — wie
/// <c>DrawBuilder</c> und <c>ParticipantResolver</c> auch. Die Dienste sollen
/// mit dem echten Chronisten laufen: was er schreibt, ist Teil dessen, was sie
/// tun, und eine Attrappe an seiner Stelle prüfte, dass sie eine Attrappe
/// aufrufen.
/// </summary>
public sealed class InMemoryFeedRepository : IFeedRepository
{
    private readonly List<TournamentPost> _posts = [];

    public IReadOnlyList<TournamentPost> All => _posts;

    public Task<IReadOnlyList<TournamentPost>> ListAsync(
        Guid tournamentId,
        int limit,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TournamentPost>>(
            [.. _posts
                .Where(post => post.TournamentId == tournamentId
                               && (before is null || post.CreatedAt < before))
                .OrderByDescending(post => post.CreatedAt)
                .Take(limit)]);

    public Task<TournamentPost?> FindAsync(Guid postId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_posts.FirstOrDefault(post => post.Id == postId));

    public void Add(TournamentPost post) => _posts.Add(post);

    public void Remove(TournamentPost post) => _posts.Remove(post);
}

/// <summary>
/// Der Push, den niemand hört. Er zählt mit, damit ein Test die Frage stellen
/// kann, ob überhaupt einer hinausgegangen wäre.
/// </summary>
public sealed class RecordingTournamentNotifier : ITournamentNotifier
{
    public List<Guid> ProjectionChanges { get; } = [];

    public List<Guid> FeedChanges { get; } = [];

    public Task ProjectionChangedAsync(
        Guid tournamentId,
        string etag,
        CancellationToken cancellationToken = default)
    {
        ProjectionChanges.Add(tournamentId);
        return Task.CompletedTask;
    }

    public Task FeedChangedAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        FeedChanges.Add(tournamentId);
        return Task.CompletedTask;
    }
}

/// <summary>Eine Uhr, die steht — damit ein Zeitstempel im Test vergleichbar bleibt.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset? now = null) =>
        Now = now ?? new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Now { get; set; }
}
