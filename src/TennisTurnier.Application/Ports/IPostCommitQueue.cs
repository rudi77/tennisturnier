namespace TennisTurnier.Application.Ports;

/// <summary>
/// Sammelt Handlungen, die erst nach einem erfolgreichen Speichern passieren
/// dürfen.
///
/// Der Anlass ist der Push an die Zuschauer: er darf nicht hinausgehen, bevor
/// die Änderung wirklich steht, sonst holen sich alle einen Stand, den es nach
/// einem Rollback nie gab. Ihn von Hand hinter jedes <c>SaveChanges</c> zu
/// schreiben wäre dasselbe in unzuverlässig — die eine Stelle, die es vergisst,
/// fällt niemandem auf.
///
/// Deshalb hängt der Versand an der Einheit der Arbeit selbst: wer speichert,
/// löst ihn aus, ohne davon wissen zu müssen.
/// </summary>
public interface IPostCommitQueue
{
    void Enqueue(Func<CancellationToken, Task> action);

    /// <summary>
    /// Führt die gesammelten Handlungen aus und leert die Warteschlange. Ruft
    /// die Einheit der Arbeit nach erfolgreichem Speichern auf.
    /// </summary>
    Task DrainAsync(CancellationToken cancellationToken = default);
}
