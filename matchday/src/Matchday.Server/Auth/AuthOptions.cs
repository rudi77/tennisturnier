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

    /// <summary>
    /// Wer herein darf: E-Mail-Adressen, getrennt durch Komma, Semikolon oder
    /// Leerraum (ADR-0023). Leer heißt jedes Google-Konto — eine Anmeldung
    /// allein sagt nur, wer jemand ist, nicht, ob er hier etwas anlegen darf.
    /// </summary>
    public string? AllowedEmails { get; set; }

    /// <summary>
    /// Darf dieses Konto herein? Gezählt wird nur eine Adresse, die Google
    /// bestätigt hat: Eine unbestätigte ließe sich auf jede beliebige setzen.
    /// </summary>
    public bool Allows(string? email, bool verified)
    {
        var allowed = (AllowedEmails ?? string.Empty)
            .Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        if (allowed.Length == 0)
        {
            return true;
        }

        return verified
            && !string.IsNullOrWhiteSpace(email)
            && allowed.Contains(email.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Wohin die Schlüssel des Sitzungs-Cookies gehören. Im Bild
    /// <c>/data/keys</c>, neben die Datenbank; leer heißt: dorthin, wo ASP.NET
    /// sie von selbst ablegt — für die Entwicklung genug.
    /// </summary>
    public string? KeysPath { get; set; }

    /// <summary>Das Schema, das zwischen Sitzung und Google-Token wählt.</summary>
    public const string Scheme = "matchday";

    /// <summary>Der Aussteller, den Google für Id-Token verwendet.</summary>
    public const string GoogleIssuer = "https://accounts.google.com";

    /// <summary>
    /// Steht die Anmeldung? Verlangt sie ohne Client-Id zu fordern, hieße:
    /// jeden abzuweisen, weil kein Token je gültig sein kann.
    /// </summary>
    public bool IsConfigured => !Required || !string.IsNullOrWhiteSpace(GoogleClientId);
}
