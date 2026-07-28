using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

public sealed class PlayerService : IPlayerService
{
    private readonly IPlayerRepository _players;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;

    public PlayerService(IPlayerRepository players, IUnitOfWork unitOfWork, IUserContext userContext)
    {
        _players = players;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
    }

    public async Task<Guid> CreatePlayerAsync(
        CreatePlayerRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireAnyClubManagement();

        var player = new Player(
            Guid.NewGuid(),
            request.FirstName,
            request.LastName,
            new PlayerContact(request.Email, request.Phone, request.DateOfBirth));

        _players.Add(player);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return player.Id;
    }

    public async Task<IReadOnlyList<PlayerSummary>> SearchAsync(
        string term,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        RequireAnyClubManagement();

        if (string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        var players = await _players.SearchAsync(term.Trim(), Math.Clamp(limit, 1, 100), cancellationToken);

        // Bewusst nur der Anzeigename: die Suche dient dem Auffinden beim Melden,
        // nicht der Einsicht in Kontaktdaten (ADR-0008).
        return players.Select(p => new PlayerSummary(p.Id, p.DisplayName)).ToList();
    }

    public async Task<PlayerDetail> GetAsync(
        Guid clubId,
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var player = await _players.FindAsync(playerId, cancellationToken)
            ?? throw new NotFoundException("Spieler", playerId);

        // Spieler fallen nicht unter den Query-Filter (ADR-0008). Der Schutz
        // muss deshalb hier entstehen, und zwar gebunden an einen Verein: wer
        // dort Interna sehen darf, darf auch die Kontaktdaten sehen.
        _userContext.Current.Require(Permission.ViewInternals, ResourceScope.Club(clubId));

        return new PlayerDetail(
            player.Id,
            player.FirstName,
            player.LastName,
            player.Contact.Email,
            player.Contact.Phone,
            player.Contact.DateOfBirth);
    }

    public async Task<ParticipantSummary> CreateParticipantAsync(
        CreateParticipantRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireAnyClubManagement();

        var first = await LoadPlayer(request.FirstPlayerId, cancellationToken);

        Participant participant;
        if (request.SecondPlayerId is { } secondId)
        {
            var second = await LoadPlayer(secondId, cancellationToken);
            participant = Participant.Team(
                Guid.NewGuid(), first.Id, second.Id, $"{first.DisplayName} / {second.DisplayName}");
        }
        else
        {
            participant = Participant.Single(Guid.NewGuid(), first.Id, first.DisplayName);
        }

        _players.Add(participant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ParticipantSummary(participant.Id, participant.DisplayName, participant.PlayerIds);
    }

    private async Task<Player> LoadPlayer(Guid playerId, CancellationToken cancellationToken) =>
        await _players.FindAsync(playerId, cancellationToken)
        ?? throw new NotFoundException("Spieler", playerId);

    /// <summary>
    /// Spieler und Teilnehmer gehören keinem Verein. Anlegen darf sie deshalb,
    /// wer irgendwo Turniere verwaltet — eine feinere Regel wäre hier nicht
    /// begründbar, weil ein Gastspieler gerade nicht dem eigenen Verein angehört.
    /// </summary>
    private void RequireAnyClubManagement()
    {
        var user = _userContext.Current;

        var mayManage = user.IsSystemAdmin
            || user.ClubIds.Any(clubId => user.Can(Permission.ManageTournament, ResourceScope.Club(clubId)));

        if (!mayManage)
        {
            throw new AccessDeniedException(Permission.ManageTournament, [ResourceScope.Global]);
        }
    }
}
