using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

/// <summary>
/// Die Aufstellungen, die in einem Turnier bereits gemeldet sind.
///
/// Nachgeschlagen wird über die Spieler und nicht über den Teilnehmer:
/// derselbe Mensch, der zweimal absendet, bekommt beim zweiten Mal denselben
/// aufgelösten Spieler, aber es entstünde ein zweiter Teilnehmer. Die
/// Reihenfolge im Doppel bleibt außer Betracht — „Müller / Berger" und
/// „Berger / Müller" sind dasselbe Paar, deshalb ist der Schlüssel sortiert.
///
/// Ein Index und keine Abfrage je Anfrage: der CSV-Import stellt diese Frage
/// einmal je Zeile. Als Abfrage gestellt lud er beim <em>k</em>-ten Namen alle
/// <em>k-1</em> vorherigen Teilnehmer erneut — und fand die eben angelegten
/// ohnehin nicht, weil sie bis zum Speichern nur im Änderungsverfolger stehen.
/// <see cref="Remember"/> schließt genau diese Lücke.
/// </summary>
public sealed class EnteredLineups
{
    private readonly Dictionary<string, TournamentEntry> _byLineup;

    internal EnteredLineups(Dictionary<string, TournamentEntry> byLineup) => _byLineup = byLineup;

    internal static string KeyOf(IEnumerable<Guid> playerIds) =>
        string.Join('|', playerIds.Order().Select(id => id.ToString("D")));

    private static string KeyOf(Player self, Player? partner) =>
        KeyOf(partner is null ? [self.Id] : [self.Id, partner.Id]);

    /// <summary>Die bestehende, nicht zurückgezogene Meldung dieser Aufstellung.</summary>
    public TournamentEntry? Find(Player self, Player? partner)
    {
        ArgumentNullException.ThrowIfNull(self);
        return _byLineup.GetValueOrDefault(KeyOf(self, partner));
    }

    /// <summary>
    /// Nimmt eine eben entstandene Meldung in den Index auf, damit dieselbe
    /// Aufstellung zweimal in derselben Datei nicht zweimal ins Feld kommt.
    /// </summary>
    public void Remember(Player self, Player? partner, TournamentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(entry);
        _byLineup[KeyOf(self, partner)] = entry;
    }
}

/// <summary>
/// Von Namen und E-Mail-Adressen zu Spielern, Teilnehmern und der Frage, ob
/// dieselbe Aufstellung schon gemeldet ist.
///
/// Alles davon brauchen zwei Wege in dieses System: die Selbstmeldung über den
/// Link und der CSV-Import der Turnierleitung. Sie unterscheiden sich darin,
/// wer meldet und wie das Turnier geladen wird — nicht darin, wer Anna Müller
/// ist. Zwei Fassungen dieser Fragen liefen auseinander, sobald eine von beiden
/// nachgezogen würde.
/// </summary>
public sealed class ParticipantResolver
{
    private readonly IPlayerRepository _players;

    /// <summary>
    /// Die in dieser Arbeitseinheit bereits aufgelösten Spieler.
    ///
    /// Ohne sie war die Auflösung nicht wiederholbar: der zweite Aufruf für
    /// denselben Namen fragte die Datenbank, fand den eben angelegten Spieler
    /// dort nicht — er steht bis zum Speichern nur im Änderungsverfolger — und
    /// legte einen zweiten an. In einer hochgeladenen Liste, in der derselbe
    /// Mensch zweimal vorkommt, stand er danach zweimal im Feld.
    ///
    /// Der Dienst lebt je Anfrage; der Zwischenspeicher damit auch. Innerhalb
    /// einer Anfrage ist „gleicher Name, gleiche E-Mail" dieselbe Person — das
    /// ist genau die Regel, nach der auch die Datenbank durchsucht wird.
    /// </summary>
    private readonly Dictionary<(string First, string Last, string Mail), Player> _resolved = [];

    public ParticipantResolver(IPlayerRepository players) => _players = players;

    /// <summary>
    /// Ein bestehender Spieler mit gleichem Namen und gleicher E-Mail, sonst ein
    /// neuer.
    ///
    /// Ohne das Nachschlagen legte derselbe Mensch bei jedem Turnier einen neuen
    /// Spieler an. Die Erkennung ist unscharf und weiß das (Risiko 6): zwei
    /// Namensvettern mit derselben Adresse werden zusammengeführt, zwei Adressen
    /// desselben Menschen bleiben getrennt. Der zweite Fehler ist der billigere,
    /// deshalb ist die Bedingung streng.
    /// </summary>
    public async Task<Player> ResolveAsync(
        string firstName,
        string lastName,
        string? email,
        string? phone,
        CancellationToken cancellationToken = default)
    {
        var first = Required(firstName, "Vorname");
        var last = Required(lastName, "Nachname");
        var mail = (email ?? string.Empty).Trim();

        // Ein Tupel und keine zusammengesetzte Zeichenkette: mit einem
        // Trennzeichen ergäben „Anna Maria“ + „Berger“ und „Anna“ +
        // „Maria Berger“ denselben Schlüssel, und zwei Menschen würden zu einem.
        var key = (first.ToLowerInvariant(), last.ToLowerInvariant(), mail.ToLowerInvariant());

        if (_resolved.TryGetValue(key, out var seen))
        {
            return seen;
        }

        if (mail.Length > 0
            && await _players.FindByNameAndEmailAsync(first, last, mail, cancellationToken) is { } known)
        {
            _resolved[key] = known;
            return known;
        }

        var player = new Player(
            Guid.NewGuid(),
            first,
            last,
            new PlayerContact(
                mail.Length == 0 ? null : mail,
                string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
                // Geburtsdatum wird nicht erhoben: es wird für nichts gebraucht,
                // und was nicht erhoben wird, ist weder zu schützen noch zu löschen.
                DateOfBirth: null));

        _players.Add(player);
        _resolved[key] = player;

        return player;
    }

    /// <summary>
    /// Der Teilnehmer zu einem Spieler oder einem Paar — die Einheit, die
    /// antritt.
    ///
    /// Der Anzeigename wird hier festgeschrieben, damit ein später umbenannter
    /// Spieler die Ergebnisliste eines abgeschlossenen Turniers nicht
    /// rückwirkend verändert.
    /// </summary>
    public Participant CreateParticipant(Player self, Player? partner, string? teamName)
    {
        ArgumentNullException.ThrowIfNull(self);

        var participant = partner is null
            ? Participant.Single(Guid.NewGuid(), self.Id, self.DisplayName)
            : Participant.Team(
                Guid.NewGuid(),
                self.Id,
                partner.Id,
                PlayerService.TeamDisplayName(teamName, self, partner));

        _players.Add(participant);
        return participant;
    }

    /// <summary>
    /// Die gemeldeten Aufstellungen eines Turniers — eine Abfrage, beliebig oft
    /// befragbar.
    ///
    /// Teilnehmer fallen nicht unter den Query-Filter (ADR-0008); dieser Aufruf
    /// ist deshalb auch für einen Anonymen tragfähig. Für das Turnier selbst
    /// gilt das ausdrücklich nicht — es kommt weiterhin nur über den Token.
    /// </summary>
    public async Task<EnteredLineups> LoadLineupsAsync(
        Tournament tournament,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tournament);

        var active = tournament.Entries
            .Where(entry => entry.Status != EntryStatus.Withdrawn)
            .ToList();

        if (active.Count == 0)
        {
            return new EnteredLineups([]);
        }

        var participants = await _players.FindParticipantsAsync(
            [.. active.Select(entry => entry.ParticipantId)], cancellationToken);

        var byId = participants.ToDictionary(participant => participant.Id);
        var byLineup = new Dictionary<string, TournamentEntry>();

        foreach (var entry in active)
        {
            if (byId.TryGetValue(entry.ParticipantId, out var participant))
            {
                byLineup[EnteredLineups.KeyOf(participant.PlayerIds)] = entry;
            }
        }

        return new EnteredLineups(byLineup);
    }

    private static string Required(string value, string what) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"Eine Meldung braucht einen {what}.")
            : value.Trim();
}
