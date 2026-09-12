using System.Collections.Concurrent;
using System.Threading.Channels;
using Matchday.Server.Api;

namespace Matchday.Server.Live;

/// <summary>
/// Wer eine Mitschau-Ansicht offen hat, bekommt jede Änderung geschoben.
/// Im Speicher, ein Prozess — mehr braucht es nicht (ADR-0016).
/// </summary>
public sealed class LiveHub
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<TournamentView?>>> _subscribers = new();

    public IDisposable Subscribe(Guid tournamentId, out ChannelReader<TournamentView?> reader)
    {
        var channel = Channel.CreateBounded<TournamentView?>(new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        var key = Guid.NewGuid();
        _subscribers.GetOrAdd(tournamentId, _ => new())[key] = channel;
        reader = channel.Reader;

        return new Subscription(() =>
        {
            if (_subscribers.TryGetValue(tournamentId, out var channels))
            {
                channels.TryRemove(key, out _);
            }

            channel.Writer.TryComplete();
        });
    }

    /// <summary>Eine neue Sicht — oder <c>null</c>, wenn das Turnier gelöscht wurde.</summary>
    public void Publish(Guid tournamentId, TournamentView? view)
    {
        if (!_subscribers.TryGetValue(tournamentId, out var channels))
        {
            return;
        }

        foreach (var channel in channels.Values)
        {
            channel.Writer.TryWrite(view);
        }
    }

    public int SubscriberCount(Guid tournamentId) =>
        _subscribers.TryGetValue(tournamentId, out var channels) ? channels.Count : 0;

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
