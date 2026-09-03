using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Zugriff auf das Turnier-Aggregat. Die Implementierung filtert nach den
/// Turnieren, an denen der Aufrufer eine Rolle hat (ADR-0004).
/// </summary>
public interface ITournamentRepository
{
    Task<Tournament?> FindAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>Die Turniere des Aufrufers — der Einstieg in die Oberfläche.</summary>
    Task<IReadOnlyList<TournamentHeader>> ListForCallerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Das Turnier zu einem Anmeldetoken — die einzige Abfrage, die am
    /// Query-Filter vorbeigeht.
    ///
    /// Sie muss es: der anonyme Melder hat keine Rolle an irgendeinem Turnier,
    /// der Filter blendet ihm alles aus. Der Token <em>ist</em> hier die
    /// Autorisierung, so wie bei der öffentlichen Projektion die Turnier-Id.
    /// Das ist die ausdrückliche, einzige Ausnahme, und sie liegt an genau
    /// dieser Stelle — kein zweiter Aufruf im anonymen Pfad geht über den
    /// normalen Repositoryweg (ADR-0004).
    /// </summary>
    Task<Tournament?> FindByRegistrationTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Steht dieses Turnier auch Fremden offen?
    ///
    /// Die zweite Abfrage am Query-Filter vorbei, und aus demselben Grund wie
    /// die erste: gefragt wird sie von jemandem ohne Rolle am Turnier, dem der
    /// Filter alles ausblendet — und die Antwort ist genau die Erlaubnis, ihm
    /// etwas zu zeigen. Sie liefert einen einzelnen Wahrheitswert und nicht das
    /// Turnier, damit aus der Ausnahme kein Schlupfloch wird.
    ///
    /// <c>null</c> heißt: es gibt dieses Turnier nicht.
    /// </summary>
    Task<bool?> IsPublicAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    void Add(Tournament tournament);

    /// <summary>
    /// Entfernt ein Turnier. Plätze, Platzzeiten und Meldungen gehen per
    /// Kaskade mit; was an eigenen Aggregaten daran hängt — Phasen,
    /// Platzzuweisungen, die öffentliche Projektion und die Rollen an diesem
    /// Turnier — räumt der Anwendungsfall, weil die Datenbank es teils nicht
    /// weiß und teils in der falschen Reihenfolge täte.
    /// </summary>
    void Remove(Tournament tournament);
}

/// <summary>
/// Formatvorlagen. Vorlagen ohne Verein sind die mitgelieferten Standardformate
/// und für jeden sichtbar.
/// </summary>
public interface IFormatTemplateRepository
{
    Task<FormatTemplate?> FindAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>Die mitgelieferten Vorlagen und die eigenen des Aufrufers.</summary>
    Task<IReadOnlyList<FormatTemplate>> ListForCallerAsync(CancellationToken cancellationToken = default);

    void Add(FormatTemplate template);
}

/// <summary>
/// Spieler und Teilnehmer.
///
/// Beide tragen bewusst keine <c>ClubId</c> (ADR-0008), fallen also nicht unter
/// den Query-Filter. Der Schutz der Kontaktdaten entsteht deshalb beim Abbilden
/// auf ein DTO, nicht hier.
/// </summary>
public interface IPlayerRepository
{
    Task<Player?> FindAsync(Guid playerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Player>> SearchAsync(string term, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Der Spieler mit genau diesem Namen und dieser E-Mail, sofern es ihn gibt.
    ///
    /// Der Weg der Selbstmeldung: ohne ihn legte derselbe Mensch bei jedem
    /// Turnier einen neuen Spieler an, und die Zusammenführung wäre danach
    /// Handarbeit. Die Erkennung ist bewusst streng — Namensgleichheit allein
    /// führte zwei verschiedene Menschen zusammen, und das wäre der teurere
    /// Fehler (Risiko 6).
    /// </summary>
    Task<Player?> FindByNameAndEmailAsync(
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Der Spieler, der zu diesem Konto gehört — oder <c>null</c>.
    ///
    /// Der erste Griff beim Beitritt: wer schon einmal mitgespielt hat, ist
    /// derselbe Spieler und nicht ein zweiter mit gleichem Namen. Erst wenn
    /// das nichts ergibt, wird über Name und E-Mail gesucht.
    /// </summary>
    Task<Player?> FindByUserAccountAsync(Guid userAccountId, CancellationToken cancellationToken = default);

    Task<Participant?> FindParticipantAsync(Guid participantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Participant>> FindParticipantsAsync(
        IReadOnlyCollection<Guid> participantIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ist der Spieler für dieses Turnier gemeldet?
    ///
    /// Die Frage entscheidet, wer seine Kontaktdaten sehen darf. Ohne sie wäre
    /// die Berechtigungsprüfung wertlos: das Turnier käme vom Aufrufer und hätte
    /// keinerlei Bezug zum abgefragten Spieler (ADR-0008).
    /// </summary>
    Task<bool> IsEnteredInTournamentAsync(
        Guid playerId,
        Guid tournamentId,
        CancellationToken cancellationToken = default);

    void Add(Player player);

    void Add(Participant participant);
}
