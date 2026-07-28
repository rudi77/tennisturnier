using Microsoft.EntityFrameworkCore;
using TennisTurnier.Adapters.Identity.Oidc;
using TennisTurnier.Adapters.Persistence.Sqlite;
using TennisTurnier.Adapters.Scheduling;
using TennisTurnier.Api;
using TennisTurnier.Api.Endpoints;
using TennisTurnier.Api.Realtime;
using TennisTurnier.Api.Web;
using TennisTurnier.Application;
using TennisTurnier.Application.Ports;

var builder = WebApplication.CreateBuilder(args);

// --- Composition Root: die einzige Stelle, an der die Adapter verdrahtet werden.
var oidc = builder.Configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();

builder.Services.AddApplication();
builder.Services.AddSqlitePersistence(
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=tennisturnier.db");
builder.Services.AddOidcIdentity(oidc);
builder.Services.AddHeuristicScheduling();

// Die Weboberfläche braucht ein Cookie, kein Bearer-Token. Solange kein
// Aussteller konfiguriert ist, stellt die Entwicklungsanmeldung eines aus;
// danach gilt ausschließlich der Identity Provider (siehe DevAuthentication).
if (!oidc.IsConfigured)
{
    builder.Services.AddDevAuthentication();
}

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSignalR();
builder.Services.AddScoped<ITournamentNotifier, SignalRTournamentNotifier>();

builder.Services.AddRazorPages();

// htmx schickt kein verstecktes Formularfeld mit, sondern einen Header. Der
// Name steht hier und im Layout — an einer dritten Stelle taucht er nicht auf.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Muss nach der Authentifizierung laufen: löst das Token in Benutzer und Rollen
// auf, auf denen der Query-Filter aus ADR-0004 arbeitet.
app.UseUserResolution();

app.MapRazorPages();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithName("Health");
app.MapClubEndpoints();
app.MapTournamentEndpoints();
app.MapMatchEndpoints();
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
public partial class Program;
