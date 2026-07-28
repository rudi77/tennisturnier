using Microsoft.EntityFrameworkCore;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Repositories;

public sealed class TournamentRepository : ITournamentRepository
{
    private readonly TennisTurnierDbContext _db;

    public TournamentRepository(TennisTurnierDbContext db) => _db = db;

    public Task<Tournament?> FindAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        _db.Tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId, cancellationToken);

    public async Task<IReadOnlyList<Tournament>> ListByClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default) =>
        await _db.Tournaments
            .Where(t => t.ClubId == clubId)
            .OrderByDescending(t => t.StartsOn)
            .ToListAsync(cancellationToken);

    public void Add(Tournament tournament) => _db.Tournaments.Add(tournament);
}

public sealed class FormatTemplateRepository : IFormatTemplateRepository
{
    private readonly TennisTurnierDbContext _db;

    public FormatTemplateRepository(TennisTurnierDbContext db) => _db = db;

    public Task<FormatTemplate?> FindAsync(Guid templateId, CancellationToken cancellationToken = default) =>
        _db.FormatTemplates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

    /// <summary>
    /// Die Vorlagen des Vereins und die mitgelieferten Standardformate. Der
    /// Query-Filter blendet fremde Vereinsvorlagen bereits aus; die Einschränkung
    /// hier grenzt zusätzlich auf den angefragten Verein ein, damit ein Benutzer
    /// mit mehreren Vereinen nicht die Vorlagen aller sieht.
    /// </summary>
    public async Task<IReadOnlyList<FormatTemplate>> ListForClubAsync(
        Guid clubId,
        CancellationToken cancellationToken = default) =>
        await _db.FormatTemplates
            .Where(t => t.ClubId == null || t.ClubId == clubId)
            .OrderBy(t => t.ClubId == null ? 0 : 1)
            .ToListAsync(cancellationToken);

    public void Add(FormatTemplate template) => _db.FormatTemplates.Add(template);
}

public sealed class PlayerRepository : IPlayerRepository
{
    private readonly TennisTurnierDbContext _db;

    public PlayerRepository(TennisTurnierDbContext db) => _db = db;

    public Task<Player?> FindAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        _db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);

    /// <summary>
    /// Sucht in Vor- und Nachname.
    ///
    /// Bewusst ohne <c>ToLower</c> oder eine andere Funktion auf der Spalte:
    /// SQLite und PostgreSQL behandeln Groß-/Kleinschreibung unterschiedlich,
    /// und ein Funktionsaufruf auf der Spalte macht jeden Index unbrauchbar.
    /// <c>EF.Functions.Like</c> übersetzt beide Anbieter selbst.
    /// </summary>
    public async Task<IReadOnlyList<Player>> SearchAsync(
        string term,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var pattern = $"%{Escape(term)}%";

        return await _db.Players
            .Where(p => EF.Functions.Like(p.LastName, pattern) || EF.Functions.Like(p.FirstName, pattern))
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<Participant?> FindParticipantAsync(
        Guid participantId,
        CancellationToken cancellationToken = default) =>
        _db.Participants.FirstOrDefaultAsync(p => p.Id == participantId, cancellationToken);

    public async Task<IReadOnlyList<Participant>> FindParticipantsAsync(
        IReadOnlyCollection<Guid> participantIds,
        CancellationToken cancellationToken = default) =>
        await _db.Participants
            .Where(p => participantIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

    public void Add(Player player) => _db.Players.Add(player);

    public void Add(Participant participant) => _db.Participants.Add(participant);

    /// <summary>
    /// Entschärft die Platzhalter von LIKE. Ohne das würde die Suche nach „%"
    /// jeden Spieler liefern und die nach „_" jeden mit passender Länge.
    /// </summary>
    private static string Escape(string term) =>
        term.Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
}
