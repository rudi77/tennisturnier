using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Zugriff auf das Turnier-Aggregat. Wie beim Verein filtert die Implementierung
/// nach dem Club-Scope des Aufrufers (ADR-0004).
/// </summary>
public interface ITournamentRepository
{
    Task<Tournament?> FindAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tournament>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken = default);

    void Add(Tournament tournament);
}

/// <summary>
/// Formatvorlagen. Vorlagen ohne Verein sind die mitgelieferten Standardformate
/// und für jeden sichtbar.
/// </summary>
public interface IFormatTemplateRepository
{
    Task<FormatTemplate?> FindAsync(Guid templateId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FormatTemplate>> ListForClubAsync(Guid clubId, CancellationToken cancellationToken = default);

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

    void Add(Player player);

    void Add(Participant participant);
}
