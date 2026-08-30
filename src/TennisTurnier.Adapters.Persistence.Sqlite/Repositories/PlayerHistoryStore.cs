using Microsoft.EntityFrameworkCore;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.Social;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Repositories;

/// <summary>
/// Die Spielhistorie, gerechnet statt gespeichert (ADR-0013).
///
/// Alles hier arbeitet unter dem Query-Filter, und das ist der ganze
/// Zugriffsschutz: sichtbar sind die Turniere, an denen der Aufrufer eine
/// Rolle hat, und aus ihnen wird die Bilanz gebildet. Ein
/// <c>IgnoreQueryFilters</c> hat in dieser Datei nichts verloren — es machte
/// aus dem Profil ein Fenster in fremde Turniere.
///
/// Der Aufwand hängt an der Zahl der Turniere des Aufrufers und nicht an der
/// Größe der Datenbank: sechs Abfragen, jede über den sichtbaren Ausschnitt.
/// Die Zuordnung Spieler → Teilnehmer geschieht im Speicher, weil die
/// Spielerliste eines Teilnehmers als Text vorliegt und nach ADR-0006 nicht
/// serverseitig durchsucht wird.
/// </summary>
public sealed class PlayerHistoryStore : IPlayerHistoryStore
{
    private readonly TennisTurnierDbContext _db;

    public PlayerHistoryStore(TennisTurnierDbContext db) => _db = db;

    public Task<Guid?> FindPlayerIdOfAccountAsync(
        Guid userAccountId,
        CancellationToken cancellationToken = default) =>
        _db.Players
            .AsNoTracking()
            .Where(p => p.UserAccountId == userAccountId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Guid>> PlayerIdsOfAccountsAsync(
        IReadOnlyCollection<Guid> userAccountIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userAccountIds);

        if (userAccountIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var rows = await _db.Players
            .AsNoTracking()
            .Where(p => p.UserAccountId != null && userAccountIds.Contains(p.UserAccountId.Value))
            .Select(p => new { AccountId = p.UserAccountId!.Value, PlayerId = p.Id })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.AccountId, row => row.PlayerId);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> AccountIdsOfPlayersAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerIds);

        if (playerIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var rows = await _db.Players
            .AsNoTracking()
            .Where(p => p.UserAccountId != null && playerIds.Contains(p.Id))
            .Select(p => new { PlayerId = p.Id, AccountId = p.UserAccountId!.Value })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.PlayerId, row => row.AccountId);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> DisplayNamesAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerIds);

        if (playerIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = await _db.Players
            .AsNoTracking()
            .Where(p => playerIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FirstName, p.LastName })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => $"{r.LastName}, {r.FirstName}");
    }

    public async Task<IReadOnlyList<PlayerEntry>> ListEntriesForPlayerAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadVisibleAsync(playerId, cancellationToken);

        return context.OwnEntries
            .Select(entry =>
            {
                var tournament = context.Tournaments[entry.TournamentId];

                return new PlayerEntry(
                    tournament.Id,
                    tournament.Name,
                    tournament.Discipline,
                    tournament.StartsOn,
                    tournament.EndsOn,
                    tournament.State,
                    entry.Status,
                    context.Participants[entry.ParticipantId].DisplayName);
            })
            .OrderByDescending(e => e.StartsOn ?? DateOnly.MaxValue)
            .ThenBy(e => e.TournamentName)
            .ToList();
    }

    public async Task<IReadOnlyList<PlayedMatch>> ListForPlayerAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadVisibleAsync(playerId, cancellationToken);

        if (context.OwnEntries.Count == 0)
        {
            return [];
        }

        var ownEntryIds = context.OwnEntries.Select(e => e.Id).ToHashSet();

        // Als nullbare Liste, weil die Spalte es ist: eine Seite, die noch
        // niemand besetzt, trägt dort NULL. Mit Guid statt Guid? müsste die
        // Abfrage den Wert auspacken, und ein Auspacken lässt sich nicht nach
        // SQL übersetzen.
        var ownEntryKeys = ownEntryIds.Select(id => (Guid?)id).ToList();

        // Nur entschiedene Matches, und nur die mit dieser Meldung auf einer
        // Seite. Die Menge der eigenen Meldungen ist so groß wie die Zahl der
        // Turniere, in denen der Spieler gemeldet ist — klein genug für ein
        // IN, anders als die Menge aller sichtbaren Meldungen.
        var matches = await _db.Matches
            .AsNoTracking()
            .Where(m => m.Score != null
                        && (ownEntryKeys.Contains(m.Side1.EntryId)
                            || ownEntryKeys.Contains(m.Side2.EntryId)))
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return [];
        }

        var phaseNames = await PhaseNamesAsync(
            matches.Select(m => m.PhaseId).Distinct().ToList(), cancellationToken);

        var playedAt = await PlayedAtAsync(
            matches.Select(m => m.Id).ToList(), cancellationToken);

        var result = new List<PlayedMatch>(matches.Count);

        foreach (var match in matches)
        {
            // Die Abfrage oben liefert nur entschiedene Matches — hier steht
            // immer ein Ergebnis.
            var score = match.Score!;

            // Ein Freilos wurde nie gespielt und gehört in keine Bilanz. Es ist
            // zugleich der einzige Fall, in dem einer Seite eines
            // entschiedenen Matches die Meldung fehlt; deshalb genügt diese
            // eine Prüfung, und alles darunter darf auspacken.
            if (score.Outcome == MatchOutcome.Bye)
            {
                continue;
            }

            // Über die nullbare Menge geprüft, wie in der Abfrage darüber: eine
            // Seite ohne Meldung gibt es hier nicht, und ein Ausweichwert für
            // sie wäre einer, der nie greift.
            var ownSide = ownEntryKeys.Contains(match.Side1.EntryId) ? 1 : 2;
            var ownEntryId = match.Side(ownSide).EntryId!.Value;
            var opponentEntryId = match.Side(ownSide == 1 ? 2 : 1).EntryId!.Value;

            // Beide Meldungen gehören zu einem Turnier, das der Aufrufer sieht
            // — sonst wäre das Match nicht durch den Filter gekommen. Ein
            // fehlender Schlüssel wäre ein widersprüchlicher Datenbestand und
            // soll laut scheitern, statt still eine Zeile zu verschlucken.
            var own = context.Entries[ownEntryId];
            var against = context.Entries[opponentEntryId];

            var ownParticipant = context.Participants[own.ParticipantId];
            var opponentParticipant = context.Participants[against.ParticipantId];
            var tournament = context.Tournaments[own.TournamentId];

            // Im Einzel steht hier niemand, im Doppel der andere der beiden.
            var partner = ownParticipant.PlayerIds
                .Where(id => id != playerId)
                .Select(id => (Guid?)id)
                .FirstOrDefault();

            result.Add(new PlayedMatch(
                match.Id,
                tournament.Id,
                tournament.Name,
                tournament.Discipline,
                tournament.StartsOn,
                phaseNames[match.PhaseId],
                match.Name,
                ownEntryId,
                ownParticipant.DisplayName,
                partner,
                opponentEntryId,
                opponentParticipant.DisplayName,
                opponentParticipant.PlayerIds,
                score.WinnerSide == ownSide,
                score.Outcome,
                score.Sets,
                score.SetsWonBy(ownSide),
                score.SetsWonBy(ownSide == 1 ? 2 : 1),
                playedAt.GetValueOrDefault(match.Id)));
        }

        // Jüngstes zuerst. Ohne Platzzuweisung gibt es keine Uhrzeit — dann
        // ordnet der Turnierbeginn die Turniere und die Runde die Matches
        // darin: das Finale ist später als das Viertelfinale, auch wenn
        // niemand mitgeschrieben hat, wann gespielt wurde.
        var rounds = matches.ToDictionary(m => m.Id, m => m.Round);

        return result
            .OrderByDescending(m => m.PlayedAt ?? ToInstant(m.TournamentStartsOn))
            .ThenByDescending(m => rounds[m.MatchId])
            .ThenBy(m => m.TournamentName)
            .ToList();
    }

    /// <summary>
    /// Der sichtbare Ausschnitt in einem Zug: Meldungen, Teilnehmer, Turniere —
    /// und daraus die Meldungen, hinter denen dieser Spieler steht.
    /// </summary>
    private async Task<VisibleContext> LoadVisibleAsync(
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var entries = await _db.Set<TournamentEntry>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
        {
            return VisibleContext.Empty;
        }

        // Über die Meldungen verbunden statt über ein IN mit tausend Ids: die
        // Zahl der sichtbaren Meldungen ist nicht beschränkt, die Zahl der
        // Parameter einer SQLite-Anweisung schon.
        var participants = await (
                from entry in _db.Set<TournamentEntry>()
                join participant in _db.Participants on entry.ParticipantId equals participant.Id
                select participant)
            .AsNoTracking()
            .Distinct()
            .ToListAsync(cancellationToken);

        var tournaments = await _db.Tournaments
            .AsNoTracking()
            .Select(t => new { t.Id, t.Name, t.Discipline, t.StartsOn, t.EndsOn, t.State })
            .ToListAsync(cancellationToken);

        var byId = participants.ToDictionary(p => p.Id);

        var mine = participants
            .Where(p => p.PlayerIds.Contains(playerId))
            .Select(p => p.Id)
            .ToHashSet();

        return new VisibleContext(
            entries.ToDictionary(e => e.Id),
            byId,
            tournaments.ToDictionary(
                t => t.Id,
                t => new TournamentFacts(t.Id, t.Name, t.Discipline, t.StartsOn, t.EndsOn, t.State)),
            [.. entries.Where(e => mine.Contains(e.ParticipantId))]);
    }

    /// <summary>
    /// Wann tatsächlich gespielt wurde. Bevorzugt das Ende, weil das Ergebnis
    /// dann feststand; ohne Ende der Beginn, und ohne beides gar nichts — eine
    /// erfundene Uhrzeit wäre schlechter als keine.
    /// </summary>
    private async Task<Dictionary<Guid, DateTimeOffset?>> PlayedAtAsync(
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken)
    {
        var rows = await _db.CourtAssignments
            .AsNoTracking()
            .Where(a => matchIds.Contains(a.MatchId))
            .Select(a => new { a.MatchId, a.ActualEnd, a.ActualStart })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.MatchId)
            .ToDictionary(
                group => group.Key,
                group => group.Max(r => r.ActualEnd ?? r.ActualStart));
    }

    /// <summary>
    /// Nur die Namen. Auf <see cref="Phase.Matches"/> liegt ein AutoInclude —
    /// ohne die Projektion und das ausdrückliche Abschalten lüde diese eine
    /// Zeile jedes Match jedes sichtbaren Turniers mit.
    /// </summary>
    private async Task<Dictionary<Guid, string>> PhaseNamesAsync(
        IReadOnlyCollection<Guid> phaseIds,
        CancellationToken cancellationToken) =>
        await _db.Set<Phase>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(p => phaseIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

    /// <summary>
    /// Ein Turnier ohne Beginn ist kein junges Turnier — es ist eines ohne
    /// Datum. Es sortiert deshalb ans Ende und nicht an den Anfang.
    /// </summary>
    private static DateTimeOffset ToInstant(DateOnly? day) =>
        day is { } value
            ? new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.MinValue;

    private sealed record TournamentFacts(
        Guid Id,
        string Name,
        Discipline Discipline,
        DateOnly? StartsOn,
        DateOnly? EndsOn,
        TournamentState State);

    private sealed record VisibleContext(
        Dictionary<Guid, TournamentEntry> Entries,
        Dictionary<Guid, Participant> Participants,
        Dictionary<Guid, TournamentFacts> Tournaments,
        IReadOnlyList<TournamentEntry> OwnEntries)
    {
        public static VisibleContext Empty { get; } = new(
            new Dictionary<Guid, TournamentEntry>(),
            new Dictionary<Guid, Participant>(),
            new Dictionary<Guid, TournamentFacts>(),
            []);
    }
}
