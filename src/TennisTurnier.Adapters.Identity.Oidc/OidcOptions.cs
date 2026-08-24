namespace TennisTurnier.Adapters.Identity.Oidc;

/// <summary>
/// Zugang zum Identity Provider. Dieselbe Klasse bedient Keycloak (lokal) und
/// Entra ID (Produktion) — der Unterschied ist Konfiguration, nicht Code
/// (ADR-0007).
/// </summary>
public sealed class OidcOptions
{
    public const string SectionName = "Oidc";

    /// <summary>
    /// Basisadresse des Ausstellers, etwa
    /// <c>http://localhost:8080/realms/tennisturnier</c> oder
    /// <c>https://login.microsoftonline.com/&lt;tenant&gt;/v2.0</c>.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Erwarteter Empfänger des Tokens.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Nur für die lokale Entwicklung gegen Keycloak über HTTP abschaltbar.
    /// In Produktion niemals <c>false</c>.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Der öffentliche Client, mit dem sich die Oberfläche anmeldet.
    ///
    /// Er steht hier und nicht im Bündel der Oberfläche: eine Single-Page-
    /// Anwendung, die ihre Authority einkompiliert hat, lässt sich nur noch für
    /// genau einen Aussteller ausliefern — und dasselbe Bild wäre in einer
    /// zweiten Instanz unbrauchbar. Der Server reicht die Angaben deshalb zur
    /// Laufzeit heraus (<c>/config.js</c>).
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Die Scopes, die die Oberfläche anfragt.</summary>
    public string Scope { get; set; } = "openid profile email";

    /// <summary>
    /// Ist die Anmeldung überhaupt konfiguriert? Ohne Authority läuft die
    /// Anwendung rein öffentlich — brauchbar für einen ersten Blick auf die
    /// Live-Ansicht, aber ohne jede schreibende Funktion.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Authority);
}
