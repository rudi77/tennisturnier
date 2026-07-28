using Microsoft.EntityFrameworkCore;
using TennisTurnier.Adapters.Identity.Oidc;
using TennisTurnier.Adapters.Persistence.Sqlite;
using TennisTurnier.Api;
using TennisTurnier.Api.Endpoints;
using TennisTurnier.Application;

var builder = WebApplication.CreateBuilder(args);

// --- Composition Root: die einzige Stelle, an der die Adapter verdrahtet werden.
var oidc = builder.Configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();

builder.Services.AddApplication();
builder.Services.AddSqlitePersistence(
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=tennisturnier.db");
builder.Services.AddOidcIdentity(oidc);

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

app.UseAuthentication();
app.UseAuthorization();

// Muss nach der Authentifizierung laufen: löst das Token in Benutzer und Rollen
// auf, auf denen der Query-Filter aus ADR-0004 arbeitet.
app.UseUserResolution();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithName("Health");
app.MapClubEndpoints();
app.MapTournamentEndpoints();
app.MapMatchEndpoints();

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
