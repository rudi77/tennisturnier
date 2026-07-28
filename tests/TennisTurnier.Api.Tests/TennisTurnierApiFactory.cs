using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TennisTurnier.Adapters.Persistence.Sqlite;
using Microsoft.AspNetCore.TestHost;
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

    private readonly Lock _migrationGate = new();
    private bool _migrated;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting und nicht ConfigureAppConfiguration: Letzteres fügt die
        // Quelle vor appsettings.json ein und würde davon überschrieben — die
        // Tests liefen dann gegen die produktive Datenbankdatei.
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_databasePath}");

        // Ohne Authority registriert der Identity-Adapter kein JWT-Verfahren und
        // überlässt das Feld dem Testschema.
        builder.UseSetting("Oidc:Authority", string.Empty);

        // WebApplicationFactory baut den Host zweimal. Liefe die Migration als
        // Nebeneffekt des Starts, rennten beide Läufe auf dieselbe Datei; hier
        // migriert stattdessen EnsureMigrated genau einmal.
        builder.UseSetting("Database:AutoMigrate", "false");

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

    protected override void ConfigureClient(HttpClient client)
    {
        EnsureMigrated();
        base.ConfigureClient(client);
    }

    /// <summary>Für Testaufbau, der die Datenbank ohne HTTP-Aufruf braucht.</summary>
    public IServiceScope CreateMigratedScope()
    {
        EnsureMigrated();
        return Services.CreateScope();
    }

    /// <summary>
    /// Migriert genau einmal je Fabrik. Jeder Zugriff auf die Datenbank läuft
    /// hierüber, damit die Reihenfolge der Tests keine Rolle spielt.
    /// </summary>
    private void EnsureMigrated()
    {
        lock (_migrationGate)
        {
            if (_migrated)
            {
                return;
            }

            Services.MigrateDatabaseAsync().GetAwaiter().GetResult();
            _migrated = true;
        }
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
