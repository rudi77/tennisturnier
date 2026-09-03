using TennisTurnier.Application.Ports;

namespace TennisTurnier.Application.Common;

/// <summary>
/// Die Warteschlange für einen Request. Reihenfolge bleibt erhalten, und eine
/// bereits ausgeführte Handlung läuft nicht ein zweites Mal.
/// </summary>
public sealed class PostCommitQueue : IPostCommitQueue
{
    private readonly List<Func<CancellationToken, Task>> _pending = [];

    private readonly IPostCommitFailures _failures;

    public PostCommitQueue(IPostCommitFailures failures) => _failures = failures;

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
            // Jede für sich. Was hier läuft, ist nicht mehr Teil des
            // Schreibvorgangs: der Commit ist durch, das Ergebnis steht in der
            // Datenbank. Eine Ausnahme durchzureichen machte daraus eine 500 auf
            // einen Aufruf, der gelungen ist — der Aufrufer versuchte es erneut
            // und bekäme beim zweiten Mal einen Konflikt. Und die übrigen
            // Handlungen, etwa der Hinweis an den Feed, fielen mit aus.
            try
            {
                await action(cancellationToken);
            }
            catch (Exception cause)
            {
                _failures.Report(cause);
            }
        }
    }
}
