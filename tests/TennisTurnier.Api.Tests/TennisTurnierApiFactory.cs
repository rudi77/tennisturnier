using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TennisTurnier.Adapters.Persistence.Sqlite;
using TennisTurnier.Application.Ports;
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
    public const string EmailHeader = "X-Test-Email";

    /// <summary>
    /// Komma-getrennte Claim-Namen, die das Testschema weglässt.
    ///
    /// Ein echtes Token garantiert nicht, was drinsteht: Entra ID legt keine
    /// E-Mail hinein, manche Aussteller kein <c>name</c>, und ein falsch
    /// konfiguriertes Verfahren gar kein <c>sub</c>. Womit die Auflösung dann
    /// umgeht, lässt sich nur prüfen, wenn der Test es weglassen kann.
    /// </summary>
    public const string OmitHeader = "X-Test-Omit";
    public const string TestIssuer = "https://test.local/realms/tennisturnier";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"tennisturnier-api-{Guid.NewGuid():N}.db");

    /// <summary>
    /// Praktisch aus: ein Testlauf stellt in Sekunden mehr Anfragen an die
    /// anonymen Meldeendpunkte als ein Turnierwochenende. Wer die Schranke
    /// selbst prüfen will, baut eine eigene Fabrik mit einer kleinen Zahl.
    /// </summary>
    private const int Unbegrenzt = 100_000;

    private readonly IReadOnlyList<string> _bootstrapSystemAdmins;
    private readonly int _publicRegistrationLimit;
    private readonly int? _teamDrawSeed;
    private readonly bool _openAccess;

    private readonly Lock _migrationGate = new();
    private bool _migrated;

    public TennisTurnierApiFactory()
        : this([])
    {
    }

    /// <summary>
    /// Fabrik mit vorab konfigurierten Systemadministratoren oder einer eigenen
    /// Ratenbegrenzung. Ein Test, der das braucht, baut sie selbst und entsorgt
    /// sie — als Klassenfixture bekäme jeder andere Test die Einstellung mit.
    ///
    /// Bewusst nicht öffentlich: xUnit lässt für eine Klassenfixture genau einen
    /// öffentlichen Konstruktor zu, und das muss der parameterlose bleiben.
    /// </summary>
    internal TennisTurnierApiFactory(
        IReadOnlyList<string> bootstrapSystemAdmins,
        int publicRegistrationLimit = Unbegrenzt,
        int? teamDrawSeed = null,
        bool openAccess = false)
    {
        _bootstrapSystemAdmins = bootstrapSystemAdmins;
        _publicRegistrationLimit = publicRegistrationLimit;
        _teamDrawSeed = teamDrawSeed;
        _openAccess = openAccess;
    }

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

        builder.UseSetting(
            "Security:PublicRegistrationRequestsPerWindow",
            _publicRegistrationLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (_teamDrawSeed is { } saat)
        {
            builder.UseSetting(
                "Tournament:TeamDrawSeed",
                saat.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (_openAccess)
        {
            builder.UseSetting("Security:OpenAccess", "true");
        }

        for (var i = 0; i < _bootstrapSystemAdmins.Count; i++)
        {
            builder.UseSetting($"Security:BootstrapSystemAdmins:{i}", _bootstrapSystemAdmins[i]);
        }

        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(HeaderAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                    HeaderAuthenticationHandler.SchemeName,
                    _ => { });

            services.AddSingleton<IClock>(Clock);
        });
    }

    /// <summary>
    /// Die Uhr, auf die sich der Turniertag bezieht.
    ///
    /// Ohne sie liegt jedes Testturnier in der Vergangenheit der Systemuhr, und
    /// alles, was „ab jetzt" rechnet, springt in die Gegenwart. Eine Zusage für
    /// 14 Uhr des Turniertags wäre dann längst verstrichen — und ein Test, der
    /// prüft, dass sie nicht unterlaufen wird, bewiese nichts.
    /// </summary>
    public MutableClock Clock { get; } = new();

    /// <summary>Ein Client, der als der angegebene Benutzer auftritt.</summary>
    public HttpClient CreateClientAs(string subject, string? email = null, string? ohneClaims = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(IssuerHeader, TestIssuer);

        if (email is not null)
        {
            client.DefaultRequestHeaders.Add(EmailHeader, email);
        }

        if (ohneClaims is not null)
        {
            client.DefaultRequestHeaders.Add(OmitHeader, ohneClaims);
        }

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
            Services.SeedBuiltInFormatsAsync().GetAwaiter().GetResult();
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

        // Hier stand einmal SqliteConnection.ClearAllPools(), damit sich die
        // Datei anschließend löschen ließ. Der Verbindungspool ist aber
        // prozessweit, und diese Fabrik ist es nicht: die Testklassen laufen
        // nebeneinander, jede mit ihrer eigenen Datei. Wer beim Aufräumen alle
        // Pools leerte, nahm den noch laufenden Klassen ihre Verbindungen weg —
        // deren nächster Zugriff endete in einem 500, und zwar in einem Test,
        // der mit der Ursache nichts zu tun hatte. Das war der Grund, aus dem
        // etwa jeder vierte Lauf an wechselnder Stelle rot war.
        //
        // Die Datei bleibt deshalb liegen, solange der Pool sie hält. Sie liegt
        // im Temp-Verzeichnis, ist ein paar Kilobyte groß und trägt einen
        // eindeutigen Namen; das ist der günstigere Preis.
        foreach (var file in new[] { _databasePath, $"{_databasePath}-shm", $"{_databasePath}-wal" })
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
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

            var claims = new List<Claim>
            {
                new("sub", subject[0]!, ClaimValueTypes.String, issuer),
                new("iss", issuer, ClaimValueTypes.String, issuer),
                new("name", subject[0]!, ClaimValueTypes.String, issuer),

                // Keycloak legt beides hinein, Entra ID nur das eine. Die
                // Auflösung nimmt „name" und fällt auf dieses zurück.
                new("preferred_username", $"{subject[0]}@kennung", ClaimValueTypes.String, issuer),
            };

            // Optional, weil das echte Token sie auch nicht garantiert: nicht
            // jeder Aussteller legt eine E-Mail hinein.
            if (Request.Headers.TryGetValue(EmailHeader, out var email) && email.Count > 0)
            {
                claims.Add(new Claim("email", email[0]!, ClaimValueTypes.String, issuer));
            }

            if (Request.Headers.TryGetValue(OmitHeader, out var omit) && omit.Count > 0)
            {
                var weggelassen = omit[0]!.Split(
                    ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                claims.RemoveAll(claim => weggelassen.Contains(claim.Type, StringComparer.Ordinal));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);

            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}

/// <summary>Eine Uhr, die der Test stellt.</summary>
public sealed class MutableClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 5, 16, 8, 0, 0, TimeSpan.FromHours(2));

    public void Advance(TimeSpan by) => Now += by;
}
