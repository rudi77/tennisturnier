namespace TennisTurnier.Application.Tournaments;

/// <summary>Anwendungsfälle rund um das Turnier — ein Driving Port (ADR-0005).</summary>
public interface ITournamentService
{
    Task<Guid> CreateAsync(Guid clubId, CreateTournamentRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TournamentSummary>> ListAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<TournamentDetail> GetAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Guid tournamentId,
        UpdateTournamentRequest request,
        CancellationToken cancellationToken = default);

    // --- Zustandsübergänge ---

    Task OpenRegistrationAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    Task CloseRegistrationAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Friert Format und Teilnehmerfeld ein. Die Formatdefinition wird dabei aus
    /// der Vorlage kopiert; spätere Änderungen an der Vorlage berühren dieses
    /// Turnier nicht mehr (ADR-0001).
    /// </summary>
    Task GenerateDrawAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>Nimmt die Auslosung zurück, um nachzumelden. Der Draw geht dabei verloren.</summary>
    Task ReopenRegistrationAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    Task StartAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    Task AbandonAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    Task SwitchToMatchDayAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    Task SwitchToPlanningAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    // --- Meldungen ---

    Task<Guid> EnterAsync(
        Guid tournamentId,
        EnterTournamentRequest request,
        CancellationToken cancellationToken = default);

    Task AcceptAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken = default);

    Task MoveToWaitingListAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken = default);

    Task WithdrawAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken = default);

    Task SetSeedAsync(
        Guid tournamentId,
        Guid entryId,
        SetSeedRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Spieler- und Teilnehmerverwaltung.</summary>
public interface IPlayerService
{
    Task<Guid> CreatePlayerAsync(CreatePlayerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suche nach Anzeigename. Liefert keine Kontaktdaten — sie dient dem
    /// Auffinden beim Melden, nicht der Einsicht (ADR-0008).
    /// </summary>
    Task<IReadOnlyList<PlayerSummary>> SearchAsync(
        string term,
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vollständige Spielerdaten inklusive Kontakt. Setzt <c>ViewInternals</c> im
    /// angegebenen Verein voraus.
    /// </summary>
    Task<PlayerDetail> GetAsync(Guid clubId, Guid playerId, CancellationToken cancellationToken = default);

    Task<ParticipantSummary> CreateParticipantAsync(
        CreateParticipantRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Formatvorlagen (ADR-0001).</summary>
public interface IFormatTemplateService
{
    Task<IReadOnlyList<FormatTemplateSummary>> ListAsync(
        Guid clubId,
        CancellationToken cancellationToken = default);

    Task<FormatTemplateDetail> GetAsync(Guid templateId, CancellationToken cancellationToken = default);

    Task<Guid> CreateAsync(
        Guid clubId,
        SaveFormatTemplateRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Guid templateId,
        SaveFormatTemplateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Legt eine bearbeitbare Kopie an — der Weg, ein Standardformat abzuwandeln.</summary>
    Task<Guid> CopyAsync(
        Guid clubId,
        Guid templateId,
        CopyFormatTemplateRequest request,
        CancellationToken cancellationToken = default);
}
