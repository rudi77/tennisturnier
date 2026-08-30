using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Social;

namespace TennisTurnier.Application.Social;

public interface IPlayDateService
{
    /// <summary>
    /// Die Verabredungen des Aufrufers — die er ausrichtet und die, zu denen er
    /// eingeladen ist. Nächster Termin zuerst.
    /// </summary>
    Task<IReadOnlyList<PlayDateView>> ListMineAsync(
        bool includePast = false,
        CancellationToken cancellationToken = default);

    Task<PlayDateView> CreateAsync(
        CreatePlayDateRequest request,
        CancellationToken cancellationToken = default);

    Task<PlayDateView> InviteAsync(
        Guid playDateId,
        InviteToPlayDateRequest request,
        CancellationToken cancellationToken = default);

    Task<PlayDateView> RespondAsync(
        Guid playDateId,
        RespondToPlayDateRequest request,
        CancellationToken cancellationToken = default);

    Task<PlayDateView> CancelAsync(Guid playDateId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Spielverabredungen außerhalb jedes Turniers (ADR-0015).
///
/// Der Zugriffsschutz steht nicht in diesem Dienst, sondern im Query-Filter:
/// sichtbar ist, was der Aufrufer ausgerichtet hat oder wozu er eingeladen ist.
/// Was hier zusätzlich geprüft wird, ist nur, wer <em>ändern</em> darf — absagen
/// und einladen der Gastgeber, antworten der Eingeladene.
/// </summary>
public sealed class PlayDateService : IPlayDateService
{
    /// <summary>
    /// Von wann an ein Termin überhaupt sinnvoll ist.
    ///
    /// Eine Verabredung für gestern ist ein Tippfehler und keine Absicht. Die
    /// Toleranz nach hinten fängt den Fall ab, dass jemand um 18:03 „heute 18
    /// Uhr" einträgt, weil die anderen schon am Platz stehen.
    /// </summary>
    private static readonly TimeSpan PastTolerance = TimeSpan.FromHours(2);

    private readonly IPlayDateRepository _playDates;
    private readonly IPlayerHistoryStore _players;
    private readonly IUserDirectory _directory;
    private readonly IUserContext _userContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public PlayDateService(
        IPlayDateRepository playDates,
        IPlayerHistoryStore players,
        IUserDirectory directory,
        IUserContext userContext,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _playDates = playDates;
        _players = players;
        _directory = directory;
        _userContext = userContext;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<IReadOnlyList<PlayDateView>> ListMineAsync(
        bool includePast = false,
        CancellationToken cancellationToken = default)
    {
        RequireAuthenticated();

        var dates = await _playDates.ListForCallerAsync(
            includePast ? null : _clock.Now.Add(-PastTolerance), cancellationToken);

        return await DescribeAsync(dates, cancellationToken);
    }

    public async Task<PlayDateView> CreateAsync(
        CreatePlayDateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var me = RequireAuthenticated();

        if (request.StartsAt < _clock.Now.Add(-PastTolerance))
        {
            throw new DomainException(
                "Der Termin liegt in der Vergangenheit — so wird das nichts mehr.");
        }

        if (request.DurationMinutes is < 15 or > 480)
        {
            throw new DomainException(
                $"Eine Verabredung dauert zwischen 15 Minuten und acht Stunden, "
                + $"angegeben waren {request.DurationMinutes}.");
        }

        var playDate = new PlayDate(
            Guid.CreateVersion7(),
            me,
            request.Title,
            request.Discipline,
            request.VenueName,
            request.StartsAt,
            TimeSpan.FromMinutes(request.DurationMinutes),
            request.Note,
            _clock.Now);

        await InviteAllAsync(playDate, request.Invitees, cancellationToken);

        _playDates.Add(playDate);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await DescribeAsync(playDate, cancellationToken);
    }

    public async Task<PlayDateView> InviteAsync(
        Guid playDateId,
        InviteToPlayDateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var playDate = await LoadAsync(playDateId, cancellationToken);
        RequireHost(playDate);

        await InviteAllAsync(playDate, request.Invitees, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await DescribeAsync(playDate, cancellationToken);
    }

    public async Task<PlayDateView> RespondAsync(
        Guid playDateId,
        RespondToPlayDateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var playDate = await LoadAsync(playDateId, cancellationToken);

        playDate.Respond(RequireAuthenticated(), request.Accepted);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await DescribeAsync(playDate, cancellationToken);
    }

    public async Task<PlayDateView> CancelAsync(
        Guid playDateId,
        CancellationToken cancellationToken = default)
    {
        var playDate = await LoadAsync(playDateId, cancellationToken);
        RequireHost(playDate);

        playDate.Cancel();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await DescribeAsync(playDate, cancellationToken);
    }

    /// <summary>
    /// Lädt die genannten Spieler ein.
    ///
    /// Wer kein Konto hat, wird beim Namen genannt und die ganze Einladung
    /// abgewiesen — nicht still übergangen. Eine Einladung, die ins Leere geht,
    /// ist schlimmer als keine: der Gastgeber wartet auf eine Antwort, die
    /// niemand geben kann (ADR-0015).
    /// </summary>
    private async Task InviteAllAsync(
        PlayDate playDate,
        IReadOnlyList<Guid> invitees,
        CancellationToken cancellationToken)
    {
        var playerIds = invitees.Where(id => id != Guid.Empty).Distinct().ToList();

        if (playerIds.Count == 0)
        {
            return;
        }

        var accounts = await _players.AccountIdsOfPlayersAsync(playerIds, cancellationToken);
        var ohneKonto = playerIds.Where(id => !accounts.ContainsKey(id)).ToList();

        if (ohneKonto.Count > 0)
        {
            var namen = await _players.DisplayNamesAsync(ohneKonto, cancellationToken);

            throw new DomainException(
                "Ohne Konto lässt sich niemand einladen — "
                + string.Join(", ", ohneKonto.Select(id => namen.GetValueOrDefault(id, "unbekannt")))
                + (ohneKonto.Count == 1 ? " hat keines." : " haben keines."));
        }

        foreach (var playerId in playerIds)
        {
            playDate.Invite(Guid.CreateVersion7(), accounts[playerId], playerId);
        }
    }

    private async Task<PlayDateView> DescribeAsync(
        PlayDate playDate,
        CancellationToken cancellationToken) =>
        (await DescribeAsync([playDate], cancellationToken))[0];

    /// <summary>
    /// Namen und Spieler in einem Zug für alle Verabredungen. Je Einladung
    /// nachzuschlagen wären bei zehn Terminen mit je drei Gästen dreißig
    /// Abfragen.
    /// </summary>
    private async Task<IReadOnlyList<PlayDateView>> DescribeAsync(
        IReadOnlyList<PlayDate> dates,
        CancellationToken cancellationToken)
    {
        if (dates.Count == 0)
        {
            return [];
        }

        var userIds = dates
            .SelectMany(date => date.Invitations.Select(i => i.UserId).Append(date.HostUserId))
            .Distinct()
            .ToList();

        var accounts = (await _directory.FindManyAsync(userIds, cancellationToken))
            .ToDictionary(account => account.Id);

        var players = await _players.PlayerIdsOfAccountsAsync(userIds, cancellationToken);

        var me = _userContext.Current.UserId;
        var now = _clock.Now;

        return [.. dates.Select(date =>
        {
            var mine = date.Invitations.FirstOrDefault(i => i.UserId == me);

            return new PlayDateView(
                date.Id,
                date.Title,
                date.Discipline,
                date.VenueName,
                date.StartsAt,
                date.EndsAt,
                date.Note,
                new PlayDateAuthorView(
                    date.HostUserId,
                    NameOf(date.HostUserId, accounts),
                    // Der Gastgeber hat einen Spieler, sobald er einmal
                    // gemeldet war — sonst bleibt sein Name ein Name.
                    players.TryGetValue(date.HostUserId, out var host) ? host : null),
                [.. date.Invitations.Select(invitation => new PlayDateGuestView(
                    invitation.UserId,
                    invitation.PlayerId,
                    NameOf(invitation.UserId, accounts),
                    invitation.Response))],
                date.RequiredPlayers,
                date.Committed,
                date.Missing,
                date.IsConfirmed,
                date.IsCancelled,
                date.EndsAt < now,
                date.HostUserId == me,
                mine?.Response);
        })];
    }

    private static string NameOf(Guid userId, IReadOnlyDictionary<Guid, UserAccount> accounts) =>
        accounts.TryGetValue(userId, out var account)
            ? account.DisplayName ?? account.Email ?? "Unbekannt"
            : "Unbekannt";

    private async Task<PlayDate> LoadAsync(Guid playDateId, CancellationToken cancellationToken) =>
        await _playDates.FindAsync(playDateId, cancellationToken)
        ?? throw new NotFoundException("Verabredung", playDateId);

    /// <summary>
    /// Nur der Gastgeber lädt ein und sagt ab. Für alle anderen sieht es aus
    /// wie eine Verabredung, die es nicht gibt (ADR-0004).
    /// </summary>
    private void RequireHost(PlayDate playDate)
    {
        if (playDate.HostUserId != _userContext.Current.UserId)
        {
            throw new NotFoundException("Verabredung", playDate.Id);
        }
    }

    private Guid RequireAuthenticated()
    {
        var user = _userContext.Current;

        return user.IsAuthenticated
            ? user.UserId
            : throw new AccessDeniedException(Permission.WriteInFeed, [ResourceScope.Global]);
    }
}
