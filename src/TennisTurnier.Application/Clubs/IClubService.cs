namespace TennisTurnier.Application.Clubs;

/// <summary>
/// Anwendungsfälle rund um Verein und Plätze — ein Driving Port im Sinne von
/// ADR-0005. Die API-Schicht kennt nur dieses Interface und die Verträge, nie
/// die Domänenentitäten.
/// </summary>
public interface IClubService
{
    Task<Guid> CreateAsync(CreateClubRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClubSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<ClubDetail> GetAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task UpdateAsync(Guid clubId, UpdateClubRequest request, CancellationToken cancellationToken = default);

    Task<Guid> AddCourtAsync(Guid clubId, CreateCourtRequest request, CancellationToken cancellationToken = default);

    Task UpdateCourtAsync(
        Guid clubId,
        Guid courtId,
        UpdateCourtRequest request,
        CancellationToken cancellationToken = default);

    Task<Guid> AddAvailabilityAsync(
        Guid clubId,
        Guid courtId,
        CreateAvailabilityRequest request,
        CancellationToken cancellationToken = default);

    Task RemoveAvailabilityAsync(
        Guid clubId,
        Guid courtId,
        Guid windowId,
        CancellationToken cancellationToken = default);

    Task<Guid> AddBlockAsync(
        Guid clubId,
        Guid courtId,
        CreateBlockRequest request,
        CancellationToken cancellationToken = default);

    Task RemoveBlockAsync(
        Guid clubId,
        Guid courtId,
        Guid blockId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Freie Fenster eines Platzes im angefragten Zeitraum: Öffnungszeiten abzüglich
    /// Sperren, aufgelöst in der Zeitzone des Vereins.
    /// </summary>
    Task<IReadOnlyList<FreeWindow>> GetFreeWindowsAsync(
        Guid clubId,
        Guid courtId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
