using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Tournaments;

/// <summary>
/// Wer als Einheit an einem Turnier teilnimmt: ein Spieler im Einzel, ein Paar
/// im Doppel oder Mixed.
///
/// Das Doppel ist hier von Anfang an vorgesehen und kein Nachgedanke. Würde ein
/// Turnier direkt auf einen Spieler zeigen, zöge sich die Sonderbehandlung
/// später durch Setzliste, Spielplan — jede Ruhezeitregel gilt für beide Partner
/// — und Ergebniseingabe.
/// </summary>
public sealed class Participant : Entity
{
    private readonly List<Guid> _playerIds = [];

    private Participant(Guid id, IEnumerable<Guid> playerIds, string displayName)
        : base(id)
    {
        _playerIds.AddRange(playerIds);
        DisplayName = displayName;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private Participant(Guid id) : base(id) => DisplayName = string.Empty;

    public static Participant Single(Guid id, Guid playerId, string displayName)
    {
        if (playerId == Guid.Empty)
        {
            throw new DomainException("Ein Einzelteilnehmer braucht einen Spieler.");
        }

        return new Participant(id, [playerId], Validate(displayName));
    }

    public static Participant Team(Guid id, Guid firstPlayerId, Guid secondPlayerId, string displayName)
    {
        if (firstPlayerId == Guid.Empty || secondPlayerId == Guid.Empty)
        {
            throw new DomainException("Ein Doppel braucht zwei Spieler.");
        }

        if (firstPlayerId == secondPlayerId)
        {
            throw new DomainException("Ein Doppel braucht zwei verschiedene Spieler.");
        }

        return new Participant(id, [firstPlayerId, secondPlayerId], Validate(displayName));
    }

    /// <summary>Ein Spieler im Einzel, zwei im Doppel.</summary>
    public IReadOnlyList<Guid> PlayerIds => _playerIds;

    public bool IsTeam => _playerIds.Count > 1;

    /// <summary>
    /// Anzeigename, etwa „Müller, Anna" oder „Müller / Berger". Wird beim Melden
    /// festgeschrieben, damit ein später umbenannter Spieler die Ergebnisliste
    /// eines abgeschlossenen Turniers nicht rückwirkend verändert.
    /// </summary>
    public string DisplayName { get; private set; }

    /// <summary>
    /// Teilen sich zwei Teilnehmer einen Spieler? Entscheidend für den Spielplan:
    /// wer im Einzel und im Doppel gemeldet ist, kann nicht zeitgleich auf zwei
    /// Plätzen stehen.
    /// </summary>
    public bool SharesPlayerWith(Participant other) => _playerIds.Intersect(other._playerIds).Any();

    private static string Validate(string displayName) =>
        string.IsNullOrWhiteSpace(displayName)
            ? throw new DomainException("Ein Teilnehmer braucht einen Anzeigenamen.")
            : displayName.Trim();
}
