using TennisTurnier.Domain.Social;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Der Feed eines Turniers (ADR-0014).
///
/// Innerhalb der laufenden Arbeitseinheit, wie <see cref="IPhaseRepository"/>
/// und aus demselben Grund: ein Ereignis wird zusammen mit dem geschrieben, was
/// es meldet. Scheitert das Eintragen des Ergebnisses, entsteht auch kein
/// Eintrag — das ist die Eigenschaft, die eine nachgelagerte Warteschlange
/// nicht hätte.
///
/// Auf den Einträgen liegt der Query-Filter des Turniers; eine
/// Berechtigungsprüfung steht hier deshalb nicht.
/// </summary>
public interface IFeedRepository
{
    /// <summary>
    /// Die jüngsten Einträge eines Turniers, samt Kommentaren. <paramref name="before"/>
    /// blättert weiter zurück — die Einträge davor.
    /// </summary>
    Task<IReadOnlyList<TournamentPost>> ListAsync(
        Guid tournamentId,
        int limit,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);

    Task<TournamentPost?> FindAsync(Guid postId, CancellationToken cancellationToken = default);

    void Add(TournamentPost post);

    void Remove(TournamentPost post);
}
