using System.Collections.Concurrent;
using System.Threading.Channels;
using Matchday.Domain;

namespace Matchday.Server.Live;

/// <summary>
/// Wer eine Mitschau-Ansicht offen hat, bekommt jede Änderung geschoben.
/// Im Speicher, ein Prozess — mehr braucht es nicht (ADR-0016). Geschoben
/// wird das Turnier selbst und nicht seine Sicht: Jede Verbindung prüft
/// daran, ob ihr Link noch gilt.
/// </summary>
public sealed class LiveHub
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<Tournament?>>> _subscribers = new();

    /// <summary>
    /// Wie lange die Mitschau auf eine Änderung wartet, bevor sie ein
    /// Lebenszeichen schickt. Zwanzig Sekunden im Betrieb; die Tests drehen es
    /// herunter, damit sie nicht warten müssen.
    /// </summary>
    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromSeconds(20);

    public IDisposable Subscribe(Guid tournamentId, out ChannelReader<Tournament?> reader)
    {
        var channel = Channel.CreateBounded<Tournament?>(new BoundedChannelOptions(8)
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

    /// <summary>Der neue Stand — oder <c>null</c>, wenn das Turnier gelöscht wurde.</summary>
    public void Publish(Guid tournamentId, Tournament? tournament)
    {
        if (!_subscribers.TryGetValue(tournamentId, out var channels))
        {
            return;
        }

        foreach (var channel in channels.Values)
        {
            channel.Writer.TryWrite(tournament);
        }
    }

    public int SubscriberCount(Guid tournamentId) =>
        _subscribers.TryGetValue(tournamentId, out var channels) ? channels.Count : 0;

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
