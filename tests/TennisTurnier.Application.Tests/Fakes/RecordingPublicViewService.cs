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

    public Task<PublicTournamentSnapshot?> GetAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PublicTournamentSnapshot?>(null);

    public Task<bool> RebuildAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        _rebuilt.Add(tournamentId);
        return Task.FromResult(true);
    }

    public Task RebuildOnDemandAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        RebuildAsync(tournamentId, cancellationToken);
}
