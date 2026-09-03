using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TennisTurnier.Application.Ports;

namespace TennisTurnier.Adapters.Identity.Oidc;

public static class IdentityRegistration
{
    /// <summary>
    /// Registriert die Tokenprüfung gegen den konfigurierten Identity Provider
    /// und die Auflösung des aufrufenden Benutzers.
    ///
    /// Ohne konfigurierte Authority wird die Authentifizierung <em>nicht</em>
    /// registriert: die Anwendung startet dann rein öffentlich. Das ist besser
    /// als ein Start mit einer Prüfung, die nichts prüft.
    /// </summary>
    public static IServiceCollection AddOidcIdentity(this IServiceCollection services, OidcOptions options)
    {
        // Die Middleware braucht sie, um über die E-Mail aus dem Token zu
        // entscheiden; ohne Registrierung stünde dort eine zweite Fassung.
        services.AddSingleton(options);

        services.AddScoped<ScopedUserContext>();
        services.AddScoped<IUserContext>(sp => sp.GetRequiredService<ScopedUserContext>());
        services.AddScoped<UserResolutionMiddleware>();

        // Immer registrieren, damit UseAuthentication/UseAuthorization in der
        // Pipeline stehen bleiben können. Ohne konfigurierten Aussteller gibt es
        // schlicht kein Verfahren, mit dem sich jemand ausweisen könnte; Tests
        // hängen an dieser Stelle ihr eigenes Schema ein.
        services.AddAuthentication();
        services.AddAuthorization();

        if (!options.IsConfigured)
        {
            // Ohne Aussteller gibt es kein Verfahren, mit dem sich jemand
            // ausweisen könnte — die Autorisierung fragt aber trotzdem nach
            // einem, sobald irgendein Endpunkt einen Ausweis verlangt. Ohne
            // dieses Ersatzschema endete das in „No authenticationScheme was
            // specified": einer 500 auf einen Aufruf, dessen richtige Antwort
            // 401 ist.
            //
            // Es weist niemanden aus und weist niemanden ab; es beantwortet nur
            // die Frage, die sonst niemand beantwortet.
            services
                .AddAuthentication(OhneAussteller.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, OhneAussteller>(
                    OhneAussteller.SchemeName, _ => { });

            return services;
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority;
                jwt.Audience = options.Audience;
                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;

                // Claims behalten ihre Namen aus dem Token. Die Standardabbildung
                // auf WS-Federation-URIs macht aus „sub" einen nameidentifier und
                // erschwert jede Fehlersuche ohne Gegenwert.
                jwt.MapInboundClaims = false;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = !string.IsNullOrWhiteSpace(options.Audience),
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "name",
                    RoleClaimType = "roles",

                    // Die Voreinstellung von fünf Minuten ist für einen Turniertag
                    // großzügig; eine halbe Minute deckt Uhrenversatz ab.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        return services;
    }

    /// <summary>
    /// Muss nach <c>UseAuthentication</c> laufen und vor allem, was Vereinsdaten liest.
    /// </summary>
    public static IApplicationBuilder UseUserResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<UserResolutionMiddleware>();
}
