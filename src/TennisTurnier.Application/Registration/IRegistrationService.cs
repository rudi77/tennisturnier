namespace TennisTurnier.Application.Registration;

/// <summary>
/// Die öffentliche Selbstmeldung — der einzige schreibende Anwendungsfall ohne
/// Anmeldung.
///
/// Autorisiert wird über den Token im Pfad und sonst nichts. Das ist dieselbe
/// Bauart wie bei der öffentlichen Projektion (ADR-0003), nur mit der Turnier-Id
/// durch ein nicht zu erratendes Token ersetzt — denn hier wird geschrieben.
/// </summary>
public interface IRegistrationService
{
    /// <summary>
    /// Der Turnierkopf zum Token. Wirft <c>NotFoundException</c>, wenn es das
    /// Token nicht gibt.
    /// </summary>
    Task<PublicRegistrationView> GetAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nimmt eine Meldung entgegen.
    ///
    /// Idempotent: dieselbe Person — gleicher Name, gleiche E-Mail — mit einer
    /// nicht zurückgezogenen Meldung legt nichts Neues an und bekommt denselben
    /// Bestätigungscode. Das erschlägt den Doppelklick auf „Absenden" und die
    /// E-Mail-Enumeration in einem.
    /// </summary>
    Task<SelfRegistrationResult> RegisterAsync(
        string token,
        SelfRegistrationRequest request,
        CancellationToken cancellationToken = default);
}
