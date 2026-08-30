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

    /// <summary>
    /// „Im Feed dieses Turniers hat sich etwas getan" — und kein Wort mehr.
    ///
    /// Der Kanal ist ohne Anmeldung erreichbar, der Feed ist die Innenansicht
    /// der Gruppe (ADR-0014). Über diesen Hinweis holt der Client ab, und dort
    /// entscheidet der Query-Filter, was er bekommt.
    /// </summary>
    public const string FeedChanged = "feedChanged";

    /// <summary>
    /// Obergrenze der Abonnements je Verbindung.
    ///
    /// Der Endpunkt ist ohne Anmeldung erreichbar; ohne Grenze könnte eine
    /// einzige Verbindung beliebig viele Gruppen anlegen. Ein Zuschauer sieht
    /// ein Turnier, ein Aushang im Vereinsheim vielleicht ein paar — deutlich
    /// mehr braucht niemand.
    /// </summary>
    private const int MaxSubscriptions = 16;

    public static string GroupOf(Guid tournamentId) => $"tournament:{tournamentId}";

    private HashSet<Guid> Subscriptions =>
        Context.Items.TryGetValue(nameof(Subscriptions), out var existing) && existing is HashSet<Guid> set
            ? set
            : (HashSet<Guid>)(Context.Items[nameof(Subscriptions)] = new HashSet<Guid>());

    public async Task Subscribe(Guid tournamentId)
    {
        var subscriptions = Subscriptions;

        if (!subscriptions.Contains(tournamentId) && subscriptions.Count >= MaxSubscriptions)
        {
            throw new HubException(
                $"Eine Verbindung kann höchstens {MaxSubscriptions} Turniere gleichzeitig verfolgen.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(tournamentId));
        subscriptions.Add(tournamentId);
    }

    public async Task Unsubscribe(Guid tournamentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(tournamentId));
        Subscriptions.Remove(tournamentId);
    }
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

    public Task FeedChangedAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        _hub.Clients
            .Group(TournamentHub.GroupOf(tournamentId))
            .SendAsync(TournamentHub.FeedChanged, tournamentId, cancellationToken);
}
