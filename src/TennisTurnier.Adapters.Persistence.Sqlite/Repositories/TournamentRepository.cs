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

    /// <summary>
    /// Die Turniere, die der Aufrufer sehen darf.
    ///
    /// Ohne Einschränkung in der Abfrage: welche das sind, entscheidet allein
    /// der Query-Filter. Eine zweite Bedingung hier wäre eine zweite Antwort
    /// auf dieselbe Frage — und die, die auseinanderläuft.
    /// </summary>
    public async Task<IReadOnlyList<Tournament>> ListForCallerAsync(
        CancellationToken cancellationToken = default) =>
        await _db.Tournaments
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
    /// </summary>
    public async Task<IReadOnlyList<Player>> SearchAsync(
        string term,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var pattern = $"%{Escape(term)}%";

        return await _db.Players
            .Where(p => EF.Functions.Like(p.LastName, pattern, EscapeCharacter)
                        || EF.Functions.Like(p.FirstName, pattern, EscapeCharacter))
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

    /// <summary>
    /// Ein Spieler ist einem Verein bekannt, wenn er für eines seiner Turniere
    /// gemeldet ist — auch als Teil eines Doppels.
    ///
    /// Bewusst über <c>IgnoreQueryFilters</c>: die Frage lautet „gehört dieser
    /// Spieler zu diesem Verein", nicht „sieht der Aufrufer dieses Turnier". Die
    /// Berechtigung des Aufrufers ist an der Aufrufstelle bereits geprüft; hier
    /// würde der Filter die Antwort nur verfälschen.
    /// </summary>
    public async Task<bool> IsKnownInClubAsync(
        Guid playerId,
        Guid clubId,
        CancellationToken cancellationToken = default)
    {
        // Die Spielerliste eines Teilnehmers liegt als Text vor und ist nicht
        // durchsuchbar (ADR-0006: JSON- und Listenspalten werden nie
        // serverseitig abgefragt). Deshalb erst die Teilnehmer der Turniere
        // dieses Vereins eingrenzen und dann im Speicher prüfen — eine Menge in
        // der Größenordnung der Meldungen eines Vereins, nicht aller Spieler.
        var participantIds = await _db.Set<TournamentEntry>()
            .IgnoreQueryFilters()
            .Where(entry => _db.Tournaments
                .IgnoreQueryFilters()
                .Any(t => t.Id == entry.TournamentId && t.ClubId == clubId))
            .Select(entry => entry.ParticipantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (participantIds.Count == 0)
        {
            return false;
        }

        var participants = await _db.Participants
            .IgnoreQueryFilters()
            .Where(p => participantIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        return participants.Any(p => p.PlayerIds.Contains(playerId));
    }

    public void Add(Player player) => _db.Players.Add(player);

    public void Add(Participant participant) => _db.Participants.Add(participant);

    private const string EscapeCharacter = "\\";

    /// <summary>
    /// Entschärft die Platzhalter von LIKE über die ESCAPE-Klausel.
    ///
    /// Die Zeichenklassen-Schreibweise <c>[%]</c> ist eine Eigenheit von
    /// SQL Server und bedeutet in SQLite und PostgreSQL etwas anderes — eine
    /// Suche nach einem Namen mit Prozentzeichen fände dort nichts. ESCAPE
    /// gehört zum Standard und verhält sich überall gleich (ADR-0006).
    /// </summary>
    private static string Escape(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
