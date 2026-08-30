using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Social;

public interface IPlayerProfileService
{
    /// <summary>
    /// Das Profil eines Spielers. 404, wenn der Aufrufer mit ihm kein
    /// sichtbares Turnier teilt — kein 403, das die Existenz verriete
    /// (ADR-0004).
    /// </summary>
    Task<PlayerProfileView> GetAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Das eigene Profil. <c>null</c>, solange zum Konto noch kein Spieler
    /// gehört — wer beigetreten ist, ohne je zu melden, hat keinen.
    /// </summary>
    Task<PlayerProfileView?> GetMineAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Schreibt das eigene Profil und legt dabei den Spieler an, falls es noch
    /// keinen gibt. Die einzige Stelle, an der ein Spieler ohne Meldung
    /// entsteht.
    /// </summary>
    Task<PlayerProfileView> UpdateMineAsync(
        UpdateMyProfileRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Das Spielerprofil (ADR-0013).
///
/// Es rechnet, statt zu lesen: Bilanz, Turniere und die letzten Matches
/// entstehen bei jedem Aufruf aus dem sichtbaren Ausschnitt des Fragenden. Die
/// Zugriffsregel fällt daraus ab und steht nicht daneben — wer mit einem
/// Spieler kein sichtbares Turnier teilt, bekommt eine leere Rechnung und
/// deshalb ein 404.
/// </summary>
public sealed class PlayerProfileService : IPlayerProfileService
{
    /// <summary>
    /// So viele Matches gehen mit dem Profil hinaus.
    ///
    /// Ein Profil ist eine Übersicht und kein Archiv. Wer mehr sehen will, öffnet
    /// das Turnier — dort steht ohnehin jedes Match, und zwar mit Bracket.
    /// </summary>
    private const int RecentMatches = 25;

    private readonly IPlayerHistoryStore _history;
    private readonly IPlayerRepository _players;
    private readonly ParticipantResolver _participants;
    private readonly IUserDirectory _directory;
    private readonly IUserContext _userContext;
    private readonly IUnitOfWork _unitOfWork;

    public PlayerProfileService(
        IPlayerHistoryStore history,
        IPlayerRepository players,
        ParticipantResolver participants,
        IUserDirectory directory,
        IUserContext userContext,
        IUnitOfWork unitOfWork)
    {
        _history = history;
        _players = players;
        _participants = participants;
        _directory = directory;
        _userContext = userContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<PlayerProfileView> GetAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var player = await _players.FindAsync(playerId, cancellationToken)
            ?? throw new NotFoundException("Spieler", playerId);

        var view = await BuildAsync(player, cancellationToken);

        // Nichts Gemeinsames heißt: für diesen Aufrufer gibt es diesen Spieler
        // nicht. Das eigene Profil fällt nicht darunter — man teilt mit sich
        // selbst jedes eigene Turnier, auch wenn man noch keines gespielt hat.
        if (!view.IsSelf && view.Tournaments.Count == 0)
        {
            throw new NotFoundException("Spieler", playerId);
        }

        return view;
    }

    public async Task<PlayerProfileView?> GetMineAsync(CancellationToken cancellationToken = default)
    {
        var player = await FindMineAsync(cancellationToken);

        return player is null ? null : await BuildAsync(player, cancellationToken);
    }

    public async Task<PlayerProfileView> UpdateMineAsync(
        UpdateMyProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await RequireAccountAsync(cancellationToken);
        var profile = PlayerProfile.From(request.Bio, request.HomeClub);

        var player = await FindMineAsync(cancellationToken);

        if (player is null)
        {
            // Derselbe Weg wie beim Beitritt: erst nachschlagen, dann anlegen.
            // Wer schon einmal von einer Turnierleitung eingelesen wurde, findet
            // hier seinen eigenen Spieler wieder, statt einen zweiten daneben zu
            // bekommen.
            player = await _participants.ResolveAsync(
                request.FirstName,
                request.LastName,
                account.Email,
                phone: null,
                cancellationToken);

            player.LinkAccount(account.Id);
            _participants.Commit();
        }
        else
        {
            player.Rename(request.FirstName, request.LastName);
        }

        player.Describe(profile);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildAsync(player, cancellationToken);
    }

    private async Task<PlayerProfileView> BuildAsync(Player player, CancellationToken cancellationToken)
    {
        var matches = await _history.ListForPlayerAsync(player.Id, cancellationToken);
        var entries = await _history.ListEntriesForPlayerAsync(player.Id, cancellationToken);

        var names = await _history.DisplayNamesAsync(
            matches
                .SelectMany(m => m.OpponentPlayerIds.Append(m.Partner ?? Guid.Empty))
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList(),
            cancellationToken);

        var byTournament = matches
            .GroupBy(m => m.TournamentId)
            .ToDictionary(g => g.Key, g => (Played: g.Count(), Won: g.Count(m => m.Won)));

        var tournaments = entries
            .Select(entry =>
            {
                var counted = byTournament.GetValueOrDefault(entry.TournamentId);

                return new PlayerTournamentView(
                    entry.TournamentId,
                    entry.TournamentName,
                    entry.Discipline,
                    entry.StartsOn,
                    entry.EndsOn,
                    entry.State,
                    entry.Status,
                    entry.ParticipantDisplayName,
                    counted.Played,
                    counted.Won);
            })
            .ToList();

        var mine = _userContext.Current;

        return new PlayerProfileView(
            player.Id,
            player.DisplayName,
            player.FirstName,
            player.LastName,
            player.Profile.Bio,
            player.Profile.HomeClub,
            IsSelf: mine.IsAuthenticated && player.UserAccountId == mine.UserId,
            HasAccount: player.UserAccountId is not null,
            Record(matches, tournaments.Count),
            tournaments,
            [.. matches.Take(RecentMatches).Select(m => ToView(m, names))]);
    }

    private static PlayerRecordView Record(IReadOnlyList<PlayedMatch> matches, int tournaments) =>
        new(
            matches.Count,
            matches.Count(m => m.Won),
            matches.Count(m => !m.Won),
            tournaments,
            matches.Sum(m => m.SetsWon),
            matches.Sum(m => m.SetsLost),
            matches
                .Select(m => m.PlayedAt?.UtcDateTime is { } instant
                    ? DateOnly.FromDateTime(instant)
                    : m.TournamentStartsOn)
                .Where(day => day is not null)
                .Max());

    private static PlayerMatchView ToView(PlayedMatch match, IReadOnlyDictionary<Guid, string> names) =>
        new(
            match.MatchId,
            match.TournamentId,
            match.TournamentName,
            match.PhaseName,
            match.MatchName,
            match.OwnDisplayName,
            match.OpponentDisplayName,
            [.. match.OpponentPlayerIds.Select(id => Link(id, names))],
            match.Partner is { } partner ? Link(partner, names) : null,
            match.Won,
            match.Outcome,
            Format(match),
            match.PlayedAt);

    private static PlayerLink Link(Guid playerId, IReadOnlyDictionary<Guid, string> names) =>
        new(playerId, names.GetValueOrDefault(playerId, "Unbekannt"));

    /// <summary>
    /// Der Spielstand als Zeile. Ein Nichtantreten hat keinen — dort steht das
    /// Wort, und nicht ein leerer Platz, der wie ein fehlendes Ergebnis aussähe.
    /// </summary>
    private static string Format(PlayedMatch match) => match.Outcome switch
    {
        MatchOutcome.Walkover => "kampflos",
        MatchOutcome.Disqualification => "Disqualifikation",
        MatchOutcome.Retirement => Sets(match) is { Length: > 0 } sets ? $"{sets} (Aufgabe)" : "Aufgabe",
        _ => Sets(match),
    };

    private static string Sets(PlayedMatch match) => string.Join(' ', match.Sets);

    private async Task<Player?> FindMineAsync(CancellationToken cancellationToken)
    {
        var user = _userContext.Current;

        return user.IsAuthenticated
            ? await _players.FindByUserAccountAsync(user.UserId, cancellationToken)
            : null;
    }

    /// <summary>
    /// Das Konto des Aufrufers. Der Endpunkt verlangt die Anmeldung, und wer
    /// angemeldet ist, hat ein Konto (ADR-0007) — ein Ausweichzweig dafür wäre
    /// einer, der nie läuft.
    /// </summary>
    private async Task<UserAccount> RequireAccountAsync(CancellationToken cancellationToken)
    {
        var user = _userContext.Current;

        if (!user.IsAuthenticated)
        {
            throw new AccessDeniedException(Permission.ViewMembers, [ResourceScope.Global]);
        }

        return (await _directory.FindAsync(user.UserId, cancellationToken))!;
    }
}
