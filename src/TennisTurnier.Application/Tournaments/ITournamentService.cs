namespace TennisTurnier.Application.Tournaments;

/// <summary>Anwendungsfälle rund um das Turnier — ein Driving Port (ADR-0005).</summary>
public interface ITournamentService
{
    /// <summary>
    /// Legt ein Turnier an und macht seinen Anleger zum Turnierleiter. Der
    /// Einstieg — es braucht dafür keinen Verein und keine Vorbereitung außer
    /// einer Formatvorlage.
    /// </summary>
    Task<Guid> CreateAsync(CreateTournamentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Die Turniere, an denen der Aufrufer eine Rolle hat. Der Einstieg in die
    /// Oberfläche, seit der Verein keiner mehr ist.
    /// </summary>
    Task<IReadOnlyList<TournamentSummary>> ListMineAsync(CancellationToken cancellationToken = default);

    Task<TournamentDetail> GetAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Guid tournamentId,
        UpdateTournamentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stellt das Satzformat des Turniers ein oder nimmt es zurück. Nur bis zur
    /// Auslosung — danach steht es im eingefrorenen Format.
    /// </summary>
    Task SetMatchFormatAsync(
        Guid tournamentId,
        SetMatchFormatRequest request,
        CancellationToken cancellationToken = default);

    // --- Plätze ---

    Task<Guid> AddCourtAsync(
        Guid tournamentId,
        CreateCourtRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateCourtAsync(
        Guid tournamentId,
        Guid courtId,
        UpdateCourtRequest request,
        CancellationToken cancellationToken = default);

    Task RemoveCourtAsync(Guid tournamentId, Guid courtId, CancellationToken cancellationToken = default);

    Task<Guid> AddCourtWindowAsync(
        Guid tournamentId,
        Guid courtId,
        CreateCourtWindowRequest request,
        CancellationToken cancellationToken = default);

    Task RemoveCourtWindowAsync(
        Guid tournamentId,
        Guid courtId,
        Guid windowId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dieselbe Uhrzeitspanne an jedem Turniertag — der Weg, den ein
    /// Veranstalter tatsächlich geht. Liefert die Zahl der angelegten Fenster.
    /// </summary>
    Task<int> AddCourtWindowsAsync(
        Guid tournamentId,
        CreateCourtWindowsRequest request,
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

    /// <summary>
    /// Löscht ein Turnier mit allem, was daran hängt.
    ///
    /// Der Unterschied zum Abbruch ist der Zweck: <see cref="AbandonAsync"/>
    /// beendet ein Turnier und lässt lesbar, was gespielt wurde. Löschen
    /// lässt nichts. Es ist der Weg für das, was gar nicht hätte entstehen
    /// sollen — der Probelauf, der Tippfehler, das doppelt angelegte Turnier.
    /// </summary>
    Task DeleteAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    Task SwitchToMatchDayAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    Task SwitchToPlanningAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    // --- Anmeldelink ---

    /// <summary>
    /// Der Anmeldelink samt Bedingungen und Zählstand. Nur für die
    /// Turnierleitung: das Token ist der Schlüssel zum Melden.
    /// </summary>
    Task<RegistrationDetail> GetRegistrationAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default);

    Task ConfigureRegistrationAsync(
        Guid tournamentId,
        ConfigureRegistrationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Neues Token; das alte ist damit sofort wertlos. Der Notausgang, wenn der
    /// Link dort gelandet ist, wo er nicht hingehört.
    /// </summary>
    Task RotateRegistrationLinkAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Öffnet die Zuschaueransicht für Fremde — oder schließt sie wieder.
    /// Vorgabe ist zu (ADR-0012).
    /// </summary>
    Task SetVisibilityAsync(
        Guid tournamentId,
        SetVisibilityRequest request,
        CancellationToken cancellationToken = default);

    // --- Meldungen ---

    /// <summary>
    /// Die Meldungen zur Verwaltung. Kontaktdaten und Bestätigungscodes stehen
    /// nur darin, wenn der Aufrufer <c>ViewInternals</c> hat.
    /// </summary>
    Task<IReadOnlyList<EntryOverview>> ListEntriesAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default);

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
    /// angegebenen Turnier voraus — und dass der Spieler dort gemeldet ist.
    /// </summary>
    Task<PlayerDetail> GetAsync(Guid tournamentId, Guid playerId, CancellationToken cancellationToken = default);

    Task<ParticipantSummary> CreateParticipantAsync(
        CreateParticipantRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Formatvorlagen (ADR-0001).</summary>
public interface IFormatTemplateService
{
    /// <summary>Die mitgelieferten Vorlagen und die eigenen des Aufrufers.</summary>
    Task<IReadOnlyList<FormatTemplateSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<FormatTemplateDetail> GetAsync(Guid templateId, CancellationToken cancellationToken = default);

    Task<Guid> CreateAsync(
        SaveFormatTemplateRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Guid templateId,
        SaveFormatTemplateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Legt eine bearbeitbare Kopie an — der Weg, ein Standardformat abzuwandeln.</summary>
    Task<Guid> CopyAsync(
        Guid templateId,
        CopyFormatTemplateRequest request,
        CancellationToken cancellationToken = default);
}
