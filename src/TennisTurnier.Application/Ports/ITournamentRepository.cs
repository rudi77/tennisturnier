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
    Task<IReadOnlyList<Tournament>> ListForCallerAsync(CancellationToken cancellationToken = default);

    void Add(Tournament tournament);
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
