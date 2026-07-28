using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Fährt die echte API gegen eine eigene SQLite-Datei hoch.
///
/// Angemeldet wird über ein Testschema, das Claims aus Request-Headern baut. Der
/// Rest der Kette — Benutzerauflösung, Rollenzuweisungen, Query-Filter,
/// Anwendungsfall — läuft unverändert. Ein Test, der stattdessen den
/// <c>IUserContext</c> ersetzt, würde genau die Verdrahtung überspringen, an der
/// ein Autorisierungsfehler entstehen würde.
/// </summary>
public sealed class TennisTurnierApiFactory : WebApplicationFactory<Program>
{
    public const string SubjectHeader = "X-Test-Subject";
    public const string IssuerHeader = "X-Test-Issuer";
    public const string TestIssuer = "https://test.local/realms/tennisturnier";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"tennisturnier-api-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_databasePath}",

                // Ohne Authority registriert der Identity-Adapter kein
                // JWT-Verfahren und überlässt das Feld dem Testschema.
                ["Oidc:Authority"] = string.Empty,
            }));

        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(HeaderAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                    HeaderAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    /// <summary>Ein Client, der als der angegebene Benutzer auftritt.</summary>
    public HttpClient CreateClientAs(string subject)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(IssuerHeader, TestIssuer);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var file in new[] { _databasePath, $"{_databasePath}-shm", $"{_databasePath}-wal" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private sealed class HeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public HeaderAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(SubjectHeader, out var subject) || subject.Count == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var issuer = Request.Headers.TryGetValue(IssuerHeader, out var value) && value.Count > 0
                ? value[0]!
                : TestIssuer;

            var identity = new ClaimsIdentity(
                [
                    new Claim("sub", subject[0]!, ClaimValueTypes.String, issuer),
                    new Claim("iss", issuer, ClaimValueTypes.String, issuer),
                    new Claim("name", subject[0]!, ClaimValueTypes.String, issuer),
                ],
                SchemeName);

            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
