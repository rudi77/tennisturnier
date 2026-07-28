using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tests.Fakes;

/// <summary>
/// Turnierspeicher im Arbeitsspeicher, der den Club-Scope genauso anwendet wie
/// der echte Query-Filter (ADR-0004).
/// </summary>
public sealed class InMemoryTournamentRepository : ITournamentRepository
{
    private readonly Dictionary<Guid, Tournament> _tournaments = [];
    private readonly IUserContext _userContext;

    public InMemoryTournamentRepository(IUserContext userContext) => _userContext = userContext;

    public Tournament Seed(Tournament tournament)
    {
        _tournaments[tournament.Id] = tournament;
        return tournament;
    }

    public Task<Tournament?> FindAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Visible().FirstOrDefault(t => t.Id == tournamentId));

    public Task<IReadOnlyList<Tournament>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Tournament>>(Visible().Where(t => t.ClubId == clubId).ToList());

    public void Add(Tournament tournament) => _tournaments[tournament.Id] = tournament;

    private IEnumerable<Tournament> Visible()
    {
        var user = _userContext.Current;

        return user.IsSystemAdmin
            ? _tournaments.Values
            : _tournaments.Values.Where(t => user.ClubIds.Contains(t.ClubId));
    }
}

public sealed class InMemoryFormatTemplateRepository : IFormatTemplateRepository
{
    private readonly Dictionary<Guid, FormatTemplate> _templates = [];

    public FormatTemplate Seed(FormatTemplate template)
    {
        _templates[template.Id] = template;
        return template;
    }

    public Task<FormatTemplate?> FindAsync(Guid templateId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_templates.GetValueOrDefault(templateId));

    public Task<IReadOnlyList<FormatTemplate>> ListForClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FormatTemplate>>(
            _templates.Values.Where(t => t.ClubId is null || t.ClubId == clubId).ToList());

    public void Add(FormatTemplate template) => _templates[template.Id] = template;
}

public sealed class InMemoryPlayerRepository : IPlayerRepository
{
    private readonly Dictionary<Guid, Player> _players = [];
    private readonly Dictionary<Guid, Participant> _participants = [];

    public Player Seed(Player player)
    {
        _players[player.Id] = player;
        return player;
    }

    public Participant Seed(Participant participant)
    {
        _participants[participant.Id] = participant;
        return participant;
    }

    public Task<Player?> FindAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_players.GetValueOrDefault(playerId));

    public Task<IReadOnlyList<Player>> SearchAsync(
        string term,
        int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Player>>(
            _players.Values
                .Where(p => p.LastName.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || p.FirstName.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList());

    public Task<Participant?> FindParticipantAsync(
        Guid participantId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_participants.GetValueOrDefault(participantId));

    public Task<IReadOnlyList<Participant>> FindParticipantsAsync(
        IReadOnlyCollection<Guid> participantIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Participant>>(
            _participants.Values.Where(p => participantIds.Contains(p.Id)).ToList());

    public void Add(Player player) => _players[player.Id] = player;

    public void Add(Participant participant) => _participants[participant.Id] = participant;
}

public sealed class CountingUnitOfWork : IUnitOfWork
{
    public int SavedChanges { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SavedChanges++;
        return Task.CompletedTask;
    }
}
