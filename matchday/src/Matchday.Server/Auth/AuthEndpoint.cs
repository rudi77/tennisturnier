using System.Security.Claims;
using Matchday.Server.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Matchday.Server.Auth;

public static class AuthEndpoint
{
    /// <summary>Das Cookie der Sitzung. Es trägt, was aus dem Google-Token hier zählt.</summary>
    public const string CookieName = "matchday.session";

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

        // Das Google-Token gilt eine Stunde. Einmal hier eingelöst, wird daraus
        // eine Sitzung, die dreißig Tage trägt und sich bei jedem Aufruf
        // verlängert — sonst flöge die Turnierleitung mitten im Turnier auf
        // die Anmeldung zurück (ADR-0025).
        app.MapPost("/api/auth/session", async (HttpContext http, IOptions<AuthOptions> options) =>
        {
            if (!options.Value.Required)
            {
                throw new NotFoundException("Diese Instanz verlangt keine Anmeldung.");
            }

            var google = await http.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);

            if (!google.Succeeded)
            {
                throw new UnauthorizedException("Die Anmeldung bei Google hat nicht getragen. Bitte noch einmal.");
            }

            http.User = google.Principal;

            // Dieselbe Prüfung wie bei jedem Aufruf, Freigabeliste inklusive:
            // Ein Konto, das nicht herein darf, bekommt gar keine Sitzung.
            Endpoints.RequireLogin(http);

            var claims = new[] { ClaimTypes.NameIdentifier, ClaimTypes.Email, "email_verified", "name", "picture" }
                .Select(type => (Type: type, Value: Claim(google.Principal, type)))
                .Where(c => c.Value is not null)
                .Select(c => new Claim(c.Type, c.Value!));

            var session = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, session);

            return Results.Ok(Account(session));
        });

        // Wer bin ich? Die Oberfläche fragt beim Laden — ein 401 heißt: zur Anmeldung.
        app.MapGet("/api/auth/me", (HttpContext http) =>
        {
            Endpoints.RequireLogin(http);
            return Results.Ok(Account(http.User));
        });

        app.MapPost("/api/auth/logout", async (HttpContext http, IOptions<AuthOptions> options) =>
        {
            if (options.Value.Required)
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }

            return Results.NoContent();
        });
    }

    /// <summary>Was die Kopfzeile zeigt: Name, Adresse, Bild. Nichts davon entscheidet etwas.</summary>
    private static AccountView Account(ClaimsPrincipal user) => new(
        Claim(user, "name") ?? string.Empty,
        Claim(user, ClaimTypes.Email) ?? string.Empty,
        Claim(user, "picture") ?? string.Empty);

    /// <summary>
    /// Eine Angabe unter ihrem Namen — oder unter dem, auf den ASP.NET sie
    /// beim Lesen des Tokens abgebildet hat.
    /// </summary>
    private static string? Claim(ClaimsPrincipal user, string type) =>
        user.FindFirst(type)?.Value ?? (Kurz.TryGetValue(type, out var kurz) ? user.FindFirst(kurz)?.Value : null);

    /// <summary>Die Namen, unter denen Google die Angaben schickt, bevor ASP.NET sie abbildet.</summary>
    private static readonly Dictionary<string, string> Kurz = new()
    {
        [ClaimTypes.NameIdentifier] = "sub",
        [ClaimTypes.Email] = "email",
    };
}

public sealed record AccountView(string Name, string Email, string Picture);
