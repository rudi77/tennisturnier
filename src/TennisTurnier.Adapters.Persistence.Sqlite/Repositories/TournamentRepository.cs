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
            // Ohne Termin nach vorn: seit er optional ist, hat ein frisch angelegtes
            // Turnier keinen — und SQLite sortiert NULL unter jeden Wert. Es stand
            // damit hinter jedem vergangenen, und die Oberfläche wählt den ersten
            // Eintrag vor.
            .OrderByDescending(t => t.StartsOn ?? DateOnly.MaxValue)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Der Tokenweg — und das einzige <c>IgnoreQueryFilters</c> auf Turnieren.
    ///
    /// Der Melder ist anonym; der Filter blendet ihm jedes Turnier aus. Der
    /// Token ist hier die Autorisierung, und er geht gegen die indizierte,
    /// eindeutige Spalte. Wer eine zweite solche Abfrage hinzufügt, hebt die
    /// Grenze auf, die ADR-0004 zieht.
    /// </summary>
    public Task<Tournament?> FindByRegistrationTokenAsync(
        string token,
        CancellationToken cancellationToken = default) =>
        _db.Tournaments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Registration.Token == token, cancellationToken);

    public void Add(Tournament tournament) => _db.Tournaments.Add(tournament);

    public void Remove(Tournament tournament) => _db.Tournaments.Remove(tournament);
}

public sealed class FormatTemplateRepository : IFormatTemplateRepository
{
    private readonly TennisTurnierDbContext _db;

    public FormatTemplateRepository(TennisTurnierDbContext db) => _db = db;

    public Task<FormatTemplate?> FindAsync(Guid templateId, CancellationToken cancellationToken = default) =>
        _db.FormatTemplates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

    /// <summary>
    /// Die mitgelieferten Standardformate und die eigenen Vorlagen des
    /// Aufrufers. Welche das sind, entscheidet der Query-Filter — hier steht
    /// nur die Reihenfolge: die mitgelieferten zuerst.
    /// </summary>
    public async Task<IReadOnlyList<FormatTemplate>> ListForCallerAsync(
        CancellationToken cancellationToken = default) =>
        await _db.FormatTemplates
            .OrderBy(t => t.OwnerUserId == null ? 0 : 1)
            .ToListAsync(cancellationToken);

    public void Add(FormatTemplate template) => _db.FormatTemplates.Add(template);
}

public sealed class PlayerRepository : IPlayerRepository
{
    private readonly TennisTurnierDbContext _db;

    public PlayerRepository(TennisTurnierDbContext db) => _db = db;

    public Task<Player?> FindAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        _db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);

    public Task<Player?> FindByUserAccountAsync(
        Guid userAccountId,
        CancellationToken cancellationToken = default) =>
        _db.Players.FirstOrDefaultAsync(p => p.UserAccountId == userAccountId, cancellationToken);

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

    /// <summary>
    /// Namensgleichheit und E-Mail-Gleichheit, beides ohne Rücksicht auf
    /// Groß-/Kleinschreibung.
    ///
    /// Der Vergleich läuft über <c>EF.Functions.Like</c> ohne Platzhalter statt
    /// über <c>ToLower</c>: LIKE ist in SQLite für ASCII von Haus aus
    /// unempfindlich gegen Groß-/Kleinschreibung und bleibt indexfähig, während
    /// eine Funktion auf der Spalte jeden Index unbrauchbar machte. Die Menge
    /// ist am Ende klein genug, dass der Restvergleich im Speicher nichts
    /// kostet — und er ist der einzige, der auch außerhalb von ASCII stimmt.
    /// </summary>
    public async Task<Player?> FindByNameAndEmailAsync(
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _db.Players
            .Where(p => EF.Functions.Like(p.LastName, Escape(lastName), EscapeCharacter))
            .Take(50)
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(p =>
            string.Equals(p.FirstName, firstName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.LastName, lastName, StringComparison.OrdinalIgnoreCase)
            && p.Contact.Email is { } stored
            && string.Equals(stored, email, StringComparison.OrdinalIgnoreCase));
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
    public async Task<bool> IsEnteredInTournamentAsync(
        Guid playerId,
        Guid tournamentId,
        CancellationToken cancellationToken = default)
    {
        // Die Spielerliste eines Teilnehmers liegt als Text vor und ist nicht
        // durchsuchbar (ADR-0006: JSON- und Listenspalten werden nie
        // serverseitig abgefragt). Deshalb erst die Teilnehmer dieses Turniers
        // eingrenzen und dann im Speicher prüfen — eine Menge in der
        // Größenordnung eines Teilnehmerfelds, nicht aller Spieler.
        var participantIds = await _db.Set<TournamentEntry>()
            .IgnoreQueryFilters()
            .Where(entry => entry.TournamentId == tournamentId)
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
