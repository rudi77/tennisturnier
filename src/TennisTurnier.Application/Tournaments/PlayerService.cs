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
        RequireAnyTournamentManagement();

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
        RequireAnyTournamentManagement();

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

        // Spieler fallen nicht unter den Query-Filter (ADR-0008), der Schutz muss
        // hier entstehen — und er braucht zwei Bedingungen, nicht eine.
        //
        // Die Berechtigung allein genügt nicht: der Verein kommt aus dem Aufruf
        // und hat für sich genommen keinen Bezug zum abgefragten Spieler. Wer
        // irgendwo ViewInternals hat, könnte sonst die Kontaktdaten jedes
        // beliebigen Spielers lesen, indem er seinen eigenen Verein angibt.
        // Deshalb muss der Spieler diesem Verein auch tatsächlich bekannt sein.
        //
        // Die Prüfung steht vorübergehend im globalen Scope, weil der Verein
        // keiner mehr ist: sie trifft damit nur noch den Systemadministrator.
        // Mit dem Verein verschwindet auch dieser Zuschnitt — die Kontaktdaten
        // eines Melders gehören dann an das Turnier, für das er gemeldet ist.
        _userContext.Current.Require(Permission.ViewInternals, ResourceScope.Global);

        if (!await _players.IsKnownInClubAsync(playerId, clubId, cancellationToken))
        {
            throw new NotFoundException("Spieler", playerId);
        }

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
        RequireAnyTournamentManagement();

        var first = await LoadPlayer(request.FirstPlayerId, cancellationToken);

        Participant participant;
        if (request.SecondPlayerId is { } secondId)
        {
            var second = await LoadPlayer(secondId, cancellationToken);
            participant = Participant.Team(
                Guid.NewGuid(), first.Id, second.Id, TeamDisplayName(request.TeamName, first, second));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.TeamName))
            {
                // Nicht stillschweigend verwerfen: wer einen Teamnamen schickt,
                // meinte ein Doppel und hat den zweiten Spieler vergessen. Ein
                // ignorierter Name fiele erst im Spielplan auf.
                throw new DomainException("Ein Teamname setzt zwei Spieler voraus.");
            }

            participant = Participant.Single(Guid.NewGuid(), first.Id, first.DisplayName);
        }

        _players.Add(participant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ParticipantSummary(participant.Id, participant.DisplayName, participant.PlayerIds);
    }

    /// <summary>
    /// Der Anzeigename eines Doppels.
    ///
    /// Die Zusammensetzung liegt hier und nicht beim Aufrufer: <c>DisplayName</c>
    /// wird beim Melden festgeschrieben und taucht danach in Bracket, Tabellen
    /// und öffentlicher Ansicht auf. Gäbe jeder Client den Namen frei vor,
    /// stünde in einem Turnier „Netzroller“ und im nächsten „Müller / Berger“ —
    /// und wer nur den Teamnamen schickte, hätte einen Spielplan, aus dem nicht
    /// hervorgeht, wer spielt.
    /// </summary>
    private static string TeamDisplayName(string? teamName, Player first, Player second)
    {
        var players = $"{first.DisplayName} / {second.DisplayName}";

        return string.IsNullOrWhiteSpace(teamName)
            ? players
            : $"{teamName.Trim()} · {players}";
    }

    private async Task<Player> LoadPlayer(Guid playerId, CancellationToken cancellationToken) =>
        await _players.FindAsync(playerId, cancellationToken)
        ?? throw new NotFoundException("Spieler", playerId);

    /// <summary>
    /// Spieler und Teilnehmer gehören keinem Turnier — sie existieren
    /// übergreifend (ADR-0008) und werden angelegt, bevor eine Meldung
    /// besteht. Anlegen darf sie deshalb, wer irgendein Turnier verwaltet.
    ///
    /// Eine feinere Regel wäre hier nicht begründbar: zum Zeitpunkt des
    /// Anlegens steht noch nicht fest, für welches Turnier der Spieler gemeldet
    /// wird, und ein Gastspieler gehört ohnehin zu keinem.
    /// </summary>
    private void RequireAnyTournamentManagement()
    {
        var user = _userContext.Current;

        var mayManage = user.IsSystemAdmin
            || user.TournamentIds.Any(id =>
                user.Can(Permission.ManageTournament, ResourceScope.Tournament(id)));

        if (!mayManage)
        {
            throw new AccessDeniedException(Permission.ManageTournament, [ResourceScope.Global]);
        }
    }
}
