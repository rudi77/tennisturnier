using TennisTurnier.Domain.Clubs;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Zugriff auf das Vereins-Aggregat.
///
/// Die Implementierung filtert nach dem Club-Scope des aufrufenden Benutzers
/// (ADR-0004). Ein Verein außerhalb des eigenen Scopes ist daher nicht von einem
/// nicht existierenden zu unterscheiden — genau das ist beabsichtigt.
/// </summary>
public interface IClubRepository
{
    /// <summary>
    /// Lädt den Verein mit Plätzen, Öffnungszeiten und Sperren. Gibt <c>null</c>
    /// zurück, wenn er nicht existiert oder außerhalb des Scopes liegt.
    /// </summary>
    Task<Club?> FindAsync(Guid clubId, CancellationToken cancellationToken = default);

    /// <summary>Vereine im Scope des Aufrufers, ohne Plätze.</summary>
    Task<IReadOnlyList<Club>> ListAsync(CancellationToken cancellationToken = default);

    void Add(Club club);

    void Remove(Club club);
}
