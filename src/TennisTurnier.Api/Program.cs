using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TennisTurnier.Adapters.Identity.Oidc;
using TennisTurnier.Adapters.Persistence.Sqlite;
using TennisTurnier.Adapters.Scheduling;
using TennisTurnier.Api;
using TennisTurnier.Api.Endpoints;
using TennisTurnier.Api.Realtime;
using TennisTurnier.Application;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.Security;

var builder = WebApplication.CreateBuilder(args);

// --- Composition Root: die einzige Stelle, an der die Adapter verdrahtet werden.
var oidc = builder.Configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();
var security = builder.Configuration.GetSection(BootstrapAdminOptions.SectionName).Get<BootstrapAdminOptions>()
               ?? new BootstrapAdminOptions();

builder.Services.AddApplication(security);
builder.Services.AddSqlitePersistence(
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=tennisturnier.db");
builder.Services.AddOidcIdentity(oidc);
builder.Services.AddHeuristicScheduling();

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSignalR();
builder.Services.AddScoped<ITournamentNotifier, SignalRTournamentNotifier>();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// Nur die anonymen Meldeendpunkte werden begrenzt. Alles unter /api steht hinter
// der Anmeldung; wer dort zu viel anfragt, hat ein Konto, das man entziehen
// kann. Der Melder ohne Konto hat keines — deshalb genau hier eine Schranke, und
// deshalb keine anderswo, wo sie am Turniertag den Betrieb träfe.
//
// Ein CAPTCHA gibt es bewusst nicht: Niederschwelligkeit ist der Zweck des
// Links. Getragen wird der Missbrauchsschutz von dieser Schranke, der Kapazität,
// dem Meldeschluss, dem Zustandsautomaten und der Idempotenz zusammen.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RegistrationEndpoints.PublicPolicy, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Die IP als Partitionsschlüssel. Hinter einem Proxy ist sie
            // ungenau; genauer ginge nur mit einer Konfiguration, die davon
            // abhängt, wie die Instanz betrieben wird. Ungenau und vorhanden ist
            // hier mehr wert als genau und ungebaut.
            http.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue(
                    "Security:PublicRegistrationRequestsPerWindow",
                    Program.RegistrationRequestsPerWindow),
                Window = TimeSpan.FromMinutes(Program.RegistrationWindowMinutes),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Der Anmeldetoken steht in der Adresszeile — ohne diese Kopfzeile stünde er
// beim nächsten ausgehenden Link im Referer und damit im Protokoll eines
// fremden Servers.
//
// Vor der Ratenbegrenzung: sie beantwortet abgewiesene Anfragen selbst, und eine
// Kopfzeile, die ausgerechnet der abgewiesenen Antwort fehlt, wäre keine.
app.Use(async (context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next(context);
});

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

// Muss nach der Authentifizierung laufen: löst das Token in Benutzer und Rollen
// auf, auf denen der Query-Filter aus ADR-0004 arbeitet.
app.UseUserResolution();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithName("Health");

// „Wer bin ich, und was darf ich" — die Oberfläche muss entscheiden, welche
// Schaltfläche sie überhaupt zeigt. Ohne Anmeldung ist „niemand" die Antwort
// und kein Fehler.
app.MapGet("/api/me", async (IMeService service, CancellationToken ct) =>
    await service.GetAsync(ct) is { } me ? Results.Ok(me) : Results.NoContent())
    .WithTags("Benutzer");

app.MapTournamentEndpoints();
app.MapMatchEndpoints();
app.MapRegistrationEndpoints();
app.MapRoleEndpoints();
app.MapPublicEndpoints();
app.MapHub<TournamentHub>("/hubs/tournament");

// Für eine Vereinsanwendung mit einer SQLite-Datei ist das Wandern des Schemas
// beim Start bequem. Es ist aber ein Nebeneffekt des Startens, und sobald zwei
// Prozesse gleichzeitig starten — mehrere Instanzen, oder ein Testhost, der den
// Host zweimal baut — überholen sie einander und einer scheitert an bereits
// vorhandenen Tabellen. Deshalb ist es abschaltbar, und wer die Migration
// steuern muss, ruft DatabaseMigrator selbst auf.
if (builder.Configuration.GetValue("Database:AutoMigrate", defaultValue: true))
{
    await app.Services.MigrateDatabaseAsync();
    await app.Services.SeedBuiltInFormatsAsync();
}

app.Run();

/// <summary>
/// Sichtbar gemacht, damit <c>WebApplicationFactory&lt;Program&gt;</c> in
/// TennisTurnier.Api.Tests einen Einstiegspunkt findet.
/// </summary>
public partial class Program
{
    /// <summary>
    /// Anfragen je Zeitfenster und IP an die anonymen Meldeendpunkte.
    ///
    /// Großzügig genug für ein Doppel, das sich zu zweit vor demselben Router
    /// meldet und das Formular ein paarmal korrigiert; eng genug, dass ein
    /// Skript nicht in Ruhe durchläuft.
    ///
    /// Die Vorgabe steht hier, wo ihre Begründung steht, und nicht in der
    /// Konfiguration. Übersteuerbar ist sie trotzdem
    /// (<c>Security:PublicRegistrationRequestsPerWindow</c>) — ein Testlauf
    /// stellt in Sekunden mehr Anfragen als ein Turnierwochenende, und ohne
    /// diesen Schalter prüfte er nur noch die Schranke.
    /// </summary>
    internal const int RegistrationRequestsPerWindow = 20;

    internal const int RegistrationWindowMinutes = 10;
}
