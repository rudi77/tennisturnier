using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TennisTurnier.Adapters.Identity.Oidc;

/// <summary>
/// Das Anmeldeverfahren einer Instanz, die keines hat.
///
/// Ohne konfigurierte Authority läuft die Anwendung rein öffentlich — es gibt
/// niemanden, der ein Token ausstellen könnte. Die Autorisierung fragt
/// trotzdem nach einem Verfahren, sobald ein Endpunkt einen Ausweis verlangt,
/// und fand keines: der Aufruf endete in „No authenticationScheme was
/// specified", also einer 500 auf einen Weg, dessen richtige Antwort 401 ist.
///
/// Dieses Schema weist niemanden aus und niemanden ab. Es beantwortet nur die
/// Frage, die sonst unbeantwortet blieb — und zwar mit „nicht angemeldet",
/// was in einer Instanz ohne Aussteller für jeden zutrifft.
/// </summary>
internal sealed class OhneAussteller : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "OhneAussteller";

    public OhneAussteller(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());
}
