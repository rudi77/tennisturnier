namespace Matchday.Server.Auth;

/// <summary>
/// Ob eine Anmeldung nötig ist, und womit. Aus <c>Auth:</c> in der
/// Konfiguration, im Betrieb also <c>Auth__Required</c> und
/// <c>Auth__GoogleClientId</c> (ADR-0019).
/// </summary>
public sealed class AuthOptions
{
    /// <summary>
    /// Der Schalter. Aus heißt: alles wie bisher, der Browser weist sich mit
    /// seiner selbst vergebenen Kennung aus (ADR-0016).
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Die Client-Id aus der Google Cloud Console. Sie ist zugleich die
    /// Audience, auf die ein Token lauten muss — ein Token für eine andere
    /// Anwendung gilt hier also nicht.
    /// </summary>
    public string? GoogleClientId { get; set; }

    /// <summary>Der Aussteller, den Google für Id-Token verwendet.</summary>
    public const string GoogleIssuer = "https://accounts.google.com";

    /// <summary>
    /// Steht die Anmeldung? Verlangt sie ohne Client-Id zu fordern, hieße:
    /// jeden abzuweisen, weil kein Token je gültig sein kann.
    /// </summary>
    public bool IsConfigured => !Required || !string.IsNullOrWhiteSpace(GoogleClientId);
}
