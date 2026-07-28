using TennisTurnier.Application.Ports;

namespace TennisTurnier.Application.Common;

/// <summary>
/// Die Warteschlange für einen Request. Reihenfolge bleibt erhalten, und eine
/// bereits ausgeführte Handlung läuft nicht ein zweites Mal.
/// </summary>
public sealed class PostCommitQueue : IPostCommitQueue
{
    private readonly List<Func<CancellationToken, Task>> _pending = [];

    public void Enqueue(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _pending.Add(action);
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        // Erst leeren, dann ausführen: ein zweites Speichern im selben Request —
        // etwa weil ein Anwendungsfall in zwei Schritten arbeitet — soll dieselbe
        // Nachricht nicht noch einmal verschicken.
        var actions = _pending.ToList();
        _pending.Clear();

        foreach (var action in actions)
        {
            await action(cancellationToken);
        }
    }
}
