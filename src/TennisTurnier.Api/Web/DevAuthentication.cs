using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Web;

/// <summary>
/// Die Anmeldung der Weboberfläche, solange kein Identity Provider konfiguriert ist.
///
/// Die Oberfläche wird serverseitig gerendert und braucht deshalb ein Cookie —
/// ein Bearer-Token, das nur die API kennt, hilft einem Browser nicht weiter.
/// Registriert wird das hier bewusst nur, wenn <see cref="Adapters.Identity.Oidc.OidcOptions.IsConfigured"/>
/// falsch ist: sobald ein Aussteller eingetragen ist, gilt dessen Anmeldung, und
/// eine zweite, die jeden Namen akzeptiert, wäre eine offene Tür daneben.
/// </summary>
internal static class DevAuthentication
{
    public const string SchemeName = "DevCookie";

    /// <summary>
    /// Der Aussteller, unter dem die so angelegten Konten geführt werden. Er
    /// steht bewusst im Kontonamen, damit ein späterer Wechsel auf Keycloak die
    /// Entwicklungskonten nicht stillschweigend übernimmt.
    /// </summary>
    public const string Issuer = "dev-login";

    public static IServiceCollection AddDevAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(SchemeName)
            .AddCookie(SchemeName, options =>
            {
                options.LoginPath = "/Login";
                options.LogoutPath = "/Login";
                options.AccessDeniedPath = "/Login";
                options.Cookie.Name = "tennisturnier.dev";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
            });

        return services;
    }

    /// <summary>
    /// Baut den Anmelde-Principal und sorgt dafür, dass das Konto existiert.
    ///
    /// Wer noch keine einzige Rollenzuweisung hat, bekommt <see cref="Role.SystemAdmin"/>.
    /// Ohne diesen Schritt stünde man vor einer leeren Datenbank, in der niemand
    /// einen Verein anlegen darf — und die Rollenvergabe hat nach ADR-0007
    /// bewusst noch keinen Endpunkt. Das ist genau der Grund, warum diese
    /// Anmeldung nicht produktiv laufen darf.
    /// </summary>
    public static async Task<ClaimsPrincipal> SignInPrincipalAsync(
        IUserDirectory directory,
        string displayName,
        CancellationToken cancellationToken)
    {
        var subject = displayName.Trim().ToLowerInvariant();

        var account = await directory.EnsureAccountAsync(
            Issuer,
            subject,
            email: null,
            displayName: displayName.Trim(),
            cancellationToken);

        var assignments = await directory.GetAssignmentsAsync(account.Id, cancellationToken);
        if (assignments.Count == 0)
        {
            await directory.AssignAsync(
                new RoleAssignment(Guid.NewGuid(), account.Id, Role.SystemAdmin, ResourceScope.Global),
                cancellationToken);
        }

        // Die Claims heißen wie im Token, damit UserResolutionMiddleware
        // unverändert damit arbeitet. Dass „name" auch der Anzeigename ist, muss
        // dabei ausdrücklich dastehen — sonst sucht ClaimsIdentity.Name nach dem
        // WS-Federation-URI und die Kopfzeile bliebe leer.
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", subject),
                new Claim("iss", Issuer),
                new Claim("name", displayName.Trim()),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            nameType: "name",
            roleType: "roles");

        return new ClaimsPrincipal(identity);
    }
}
