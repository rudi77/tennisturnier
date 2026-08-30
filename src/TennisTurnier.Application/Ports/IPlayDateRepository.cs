using TennisTurnier.Domain.Social;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Verabredungen (ADR-0015).
///
/// Auf ihnen liegt ein eigener Query-Filter: sichtbar ist, was der Aufrufer
/// ausgerichtet hat oder wozu er eingeladen ist. Eine Berechtigungsprüfung
/// steht hier deshalb nicht — was er nicht sehen darf, kommt gar nicht erst
/// zurück.
/// </summary>
public interface IPlayDateRepository
{
    /// <summary>
    /// Die Verabredungen des Aufrufers, jüngster Termin zuerst.
    /// <paramref name="from"/> schneidet Vergangenes ab.
    /// </summary>
    Task<IReadOnlyList<PlayDate>> ListForCallerAsync(
        DateTimeOffset? from = null,
        CancellationToken cancellationToken = default);

    Task<PlayDate?> FindAsync(Guid playDateId, CancellationToken cancellationToken = default);

    void Add(PlayDate playDate);

    void Remove(PlayDate playDate);
}
