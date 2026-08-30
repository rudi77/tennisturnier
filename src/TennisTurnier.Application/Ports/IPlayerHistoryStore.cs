using TennisTurnier.Application.Social;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Die gespielten Matches eines Spielers — über Turniergrenzen hinweg, aber
/// niemals über die Sichtbarkeitsgrenze des Aufrufers (ADR-0013).
///
/// Ein eigener Port und kein weiteres Repository: die Frage „was hat dieser
/// Mensch gespielt" quert vier Aggregate — Turnier, Meldung, Teilnehmer,
/// Match — und wäre über deren Repositories eine Schleife über Turniere mit
/// vier Abfragen je Durchlauf. Sie ist eine Leseabfrage im Sinne von
/// ADR-0003, nur ohne eigene Tabelle: gerechnet wird beim Fragen.
///
/// Die Sicherheit steckt nicht in dieser Schnittstelle, sondern im
/// Query-Filter, unter dem ihre Implementierung arbeitet (ADR-0004). Sie darf
/// deshalb keine Ausnahme davon machen — anders als
/// <see cref="ITournamentRepository.FindByRegistrationTokenAsync"/>, wo der
/// Token die Autorisierung ist.
/// </summary>
public interface IPlayerHistoryStore
{
    /// <summary>
    /// Alle entschiedenen Matches, in denen dieser Spieler auf einer Seite
    /// stand — jüngste zuerst. Freilose sind nicht enthalten: sie wurden nie
    /// gespielt.
    /// </summary>
    Task<IReadOnlyList<PlayedMatch>> ListForPlayerAsync(
        Guid playerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Die Turniere, in denen dieser Spieler gemeldet ist — auch die noch
    /// ungespielten. Ohne sie fehlte im Profil genau das Turnier, um das es
    /// gerade geht: das, dessen erster Ball noch nicht gespielt ist.
    /// </summary>
    Task<IReadOnlyList<PlayerEntry>> ListEntriesForPlayerAsync(
        Guid playerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Der Spieler zu einem Konto, samt Anzeigename — ohne den Umweg über das
    /// Spieler-Repository, das für diese eine Frage das ganze Aggregat lädt.
    /// </summary>
    Task<Guid?> FindPlayerIdOfAccountAsync(
        Guid userAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Die Spieler zu mehreren Konten — der Weg vom Verfasser eines Beitrags in
    /// sein Profil (ADR-0014). Konten ohne Spieler fehlen im Ergebnis.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Guid>> PlayerIdsOfAccountsAsync(
        IReadOnlyCollection<Guid> userAccountIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Die Konten zu mehreren Spielern — die Gegenrichtung von
    /// <see cref="PlayerIdsOfAccountsAsync"/>.
    ///
    /// Der Weg vom Kontakt zur Einladung (ADR-0015): eingeladen wird aus dem
    /// Kontaktgraphen, und der kennt Spieler; antworten kann nur ein Konto.
    /// Spieler ohne Konto fehlen im Ergebnis, und genau daran erkennt der
    /// Anwendungsfall, wen er nicht einladen kann.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Guid>> AccountIdsOfPlayersAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken = default);

    /// <summary>Anzeigenamen zu Spieler-Ids — für Partner, Gegner und Kontakte.</summary>
    Task<IReadOnlyDictionary<Guid, string>> DisplayNamesAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken = default);
}
