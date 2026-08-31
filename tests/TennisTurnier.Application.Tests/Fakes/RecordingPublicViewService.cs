using TennisTurnier.Application.PublicView;

namespace TennisTurnier.Application.Tests.Fakes;

/// <summary>
/// Merkt sich, für welche Turniere die öffentliche Ansicht neu gebaut wurde.
///
/// Damit lässt sich prüfen, dass keine schreibende Handlung sie vergisst — ohne
/// dafür eine Datenbank zu starten. Genau dafür ist es ein Port.
/// </summary>
public sealed class RecordingPublicViewService : IPublicViewService
{
    private readonly List<Guid> _rebuilt = [];

    public IReadOnlyList<Guid> Rebuilt => _rebuilt;

    /// <summary>
    /// Wie viele Speichervorgänge es gab, als zuletzt neu gebaut wurde.
    ///
    /// Damit lässt sich festhalten, dass der Neuaufbau <em>vor</em> dem
    /// Speichern läuft und beides in einem Zug in die Datenbank geht. Liefe er
    /// danach, läse er einen Stand, den parallele Anfragen inzwischen überholt
    /// haben — und schriebe ihn als letzter fest (ADR-0003).
    /// </summary>
    public int SavesBeforeLastRebuild { get; private set; } = -1;

    /// <summary>Wird von der Attrappe der Einheit der Arbeit gesetzt.</summary>
    public Func<int>? SaveCount { get; set; }

    public Task<PublicViewLookup> GetAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PublicViewLookup(Visible: false, Snapshot: null));

    public Task<bool> RebuildAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        _rebuilt.Add(tournamentId);
        SavesBeforeLastRebuild = SaveCount?.Invoke() ?? -1;

        return Task.FromResult(true);
    }

    public Task RebuildOnDemandAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        RebuildAsync(tournamentId, cancellationToken);
}
