using TennisTurnier.Domain.PublicView;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Ablage der öffentlichen Sicht (ADR-0003).
///
/// Die einzige Ablage, die den Query-Filter aus ADR-0004 nicht anwendet — sie
/// wird von Zuschauern ohne Anmeldung gelesen. Der Schutz liegt darin, was in
/// die Projektion gelangt, nicht darin, wer sie lesen darf.
/// </summary>
public interface ITournamentProjectionStore
{
    Task<TournamentProjection?> FindAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    void Add(TournamentProjection projection);

    void Remove(TournamentProjection projection);
}

/// <summary>
/// Benachrichtigt die Zuschauer eines Turniers über einen neuen Stand.
///
/// Der Push ist bewusst nur ein Hinweis, kein Transport: die Ansicht selbst holt
/// sich der Client über den öffentlichen Endpunkt, der ohnehin für Polling da
/// ist. Damit bleibt ein Zuschauer ohne funktionierende Verbindung nicht auf
/// einem veralteten Stand sitzen.
/// </summary>
public interface ITournamentNotifier
{
    Task ProjectionChangedAsync(
        Guid tournamentId,
        string etag,
        CancellationToken cancellationToken = default);
}
