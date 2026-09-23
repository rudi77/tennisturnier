using Matchday.Server.Api;
using Microsoft.Extensions.Options;

namespace Matchday.Server.Auth;

public static class AuthEndpoint
{
    /// <summary>
    /// Was die Oberfläche wissen muss, bevor sie irgendetwas anderes tut: ob
    /// eine Anmeldung verlangt wird und mit welcher Client-Id sie Google
    /// anspricht. Der Endpunkt ist offen — er sagt nichts, was nicht ohnehin
    /// im ausgelieferten Bündel stünde, und wer sich anmelden soll, muss ihn
    /// vor der Anmeldung lesen können.
    /// </summary>
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/config", (IOptions<AuthOptions> options) => Results.Ok(new
        {
            required = options.Value.Required,
            googleClientId = options.Value.GoogleClientId ?? string.Empty,
        }));

        // Gleich nach der Anmeldung gefragt: Trägt dieses Konto hier? Sonst
        // stünde man mit einem gültigen Token vor lauter 403ern und wüsste
        // nicht, warum (ADR-0023).
        app.MapGet("/api/auth/check", (HttpContext http) =>
        {
            Endpoints.RequireLogin(http);
            return Results.NoContent();
        });
    }
}
