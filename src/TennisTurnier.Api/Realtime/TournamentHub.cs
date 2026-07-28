using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TennisTurnier.Application.Ports;

namespace TennisTurnier.Api.Realtime;

/// <summary>
/// Der Push-Kanal der öffentlichen Ansicht (ADR-0003).
///
/// Ein Zuschauer abonniert genau ein Turnier. Die Gruppe ist deshalb das
/// Turnier — ohne sie ginge jede Ergebnismeldung an alle Verbindungen, auch an
/// die eines ganz anderen Vereins.
///
/// Ohne Anmeldung, wie der Endpunkt daneben: die Nachricht enthält nur die Id
/// des Turniers und den neuen ETag, nichts, was nicht ohnehin öffentlich ist.
/// </summary>
[AllowAnonymous]
public sealed class TournamentHub : Hub
{
    public const string ProjectionChanged = "projectionChanged";

    public static string GroupOf(Guid tournamentId) => $"tournament:{tournamentId}";

    public Task Subscribe(Guid tournamentId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(tournamentId));

    public Task Unsubscribe(Guid tournamentId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(tournamentId));
}

/// <summary>
/// Der Adapter, der den Port auf SignalR legt.
///
/// Er verschickt bewusst nur einen Hinweis und nicht die Ansicht selbst: der
/// Client holt sie über den öffentlichen Endpunkt, der ohnehin für Polling da
/// ist und dessen ETag er dann schon kennt. Damit gibt es einen Weg, auf dem
/// Daten öffentlich werden, und nicht zwei.
/// </summary>
public sealed class SignalRTournamentNotifier : ITournamentNotifier
{
    private readonly IHubContext<TournamentHub> _hub;

    public SignalRTournamentNotifier(IHubContext<TournamentHub> hub) => _hub = hub;

    public Task ProjectionChangedAsync(
        Guid tournamentId,
        string etag,
        CancellationToken cancellationToken = default) =>
        _hub.Clients
            .Group(TournamentHub.GroupOf(tournamentId))
            .SendAsync(TournamentHub.ProjectionChanged, tournamentId, etag, cancellationToken);
}
