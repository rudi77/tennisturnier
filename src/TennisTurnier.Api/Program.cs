using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
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
using TennisTurnier.Application.Tournaments;

var builder = WebApplication.CreateBuilder(args);

// --- Composition Root: die einzige Stelle, an der die Adapter verdrahtet werden.
//
// Gebunden statt gelesen: Bind füllt nur, was dasteht, und lässt jede Vorgabe
// stehen, die keiner überschreibt. Ein Get<T> müsste den Fall abfangen, dass es
// den Abschnitt gar nicht gibt — appsettings.json bringt beide mit, und eine
// Ausweichfassung dafür wäre eine, die nie läuft.
var oidc = new OidcOptions();
builder.Configuration.GetSection(OidcOptions.SectionName).Bind(oidc);

var security = new BootstrapAdminOptions();
builder.Configuration.GetSection(BootstrapAdminOptions.SectionName).Bind(security);

var tournaments = new TournamentOptions();
builder.Configuration.GetSection(TournamentOptions.SectionName).Bind(tournaments);

// Anmeldung und offener Betrieb schließen einander aus. Der Fehlschlag ist
// beabsichtigt: der stille Ausgang hieße, dass ein versehentlich gesetzter
// Schalter eine angemeldete Instanz aufmacht, ohne dass jemand es merkt — und
// die Sorte Fehler zeigt sich sonst erst, wenn sie ausgenutzt wurde.
if (security.OpenAccess && oidc.IsConfigured)
{
    throw new InvalidOperationException(
        $"{BootstrapAdminOptions.SectionName}:{nameof(BootstrapAdminOptions.OpenAccess)} und "
        + $"{OidcOptions.SectionName}:{nameof(OidcOptions.Authority)} sind beide gesetzt. "
        + "Der offene Betrieb ist der Schritt vor der Anmeldung, nicht daneben — "
        + "einer von beiden gehört entfernt.");
}

builder.Services.AddApplication(security, tournaments);

// Dasselbe hier: die Verbindungszeichenfolge steht in appsettings.json.
builder.Services.AddSqlitePersistence(builder.Configuration.GetConnectionString("Default")!);
builder.Services.AddOidcIdentity(oidc);
builder.Services.AddHeuristicScheduling();

// Im offenen Betrieb ist Autorisierung keine Frage mehr.
//
// Ohne Aussteller registriert der Identity-Adapter kein Anmeldeverfahren — es
// gibt dann keines, mit dem sich jemand ausweisen könnte. Ein Endpunkt hinter
// `RequireAuthorization` verlangte trotzdem einen Ausweis, die Autorisierung
// forderte ihn an, und weil kein Verfahren da ist, das ihn ausstellt, endete
// der Aufruf mit „No authenticationScheme was specified" — einer 500 auf einen
// Weg, der einfach offenstehen sollte. Genau so war der Beitrittslink auf einer
// offenen Instanz unbenutzbar.
//
// Die Vorgabe wird deshalb erfüllbar. Das ist keine Lücke, sondern dieselbe
// Aussage wie überall sonst im offenen Betrieb: es gibt einen Benutzer, jeder
// Aufruf ist er, und die Benutzerauflösung setzt ihn (ADR-0007). Wer prüft,
// ob dieser eine Benutzer etwas darf, fragt weiterhin die Rechtematrix.
if (security.OpenAccess)
{
    builder.Services.AddAuthorization(o =>
        o.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build());
}

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSignalR();
builder.Services.AddScoped<ITournamentNotifier, SignalRTournamentNotifier>();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// Hier stand eine Ratenbegrenzung für die anonymen Meldeendpunkte. Sie ist
// mit ihnen entfallen: beitreten kann nur, wer angemeldet ist, und wer zu viel
// anfragt, hat ein Konto, das man entziehen kann. Genau das war die
// Begründung, warum überall sonst keine Schranke steht — sie gilt jetzt auch
// hier.
var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Die gebaute Oberfläche, sofern sie im Bild neben der Anwendung liegt. Im
// Entwicklungsbetrieb tut sie das nicht — dort liefert Vite sie aus und reicht
// die API hierher weiter. Ohne wwwroot passiert hier schlicht nichts.
//
// Die Oberfläche führt ihre Navigation über die Adresszeile („?screen=board"),
// nicht über Pfade. Es braucht deshalb keinen Rückfall auf index.html: die eine
// Adresse, die sie öffnet, ist die Wurzel — und die beantwortet UseDefaultFiles.
app.UseDefaultFiles();
app.UseStaticFiles();

// Der Beitrittstoken steht in der Adresszeile — ohne diese Kopfzeile stünde er
// beim nächsten ausgehenden Link im Referer und damit im Protokoll eines
// fremden Servers.
app.Use(async (context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next(context);
});

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

// Die Anmeldedaten der Oberfläche, zur Laufzeit statt einkompiliert.
//
// Eine Single-Page-Anwendung, die ihre Authority im Bündel trägt, lässt sich
// nur für genau einen Aussteller ausliefern — dasselbe Bild wäre in einer
// zweiten Instanz unbrauchbar, und ein Wechsel des Realms verlangte einen
// neuen Bau. Als Skript und nicht als JSON, damit die Oberfläche beim Laden
// schon Bescheid weiß und nicht erst nach einer Anfrage.
var oberflaechenKonfiguration = JsonSerializer.Serialize(new
{
    oidcAuthority = oidc.Authority,
    oidcClientId = oidc.ClientId,
    oidcScope = oidc.Scope,

    // Ohne diese Angabe stünde die Oberfläche vor einer Anmeldemaske, hinter
    // der es nichts anzumelden gibt: sie kann von sich aus nicht wissen, dass
    // der Server jeden Aufruf durchlässt.
    openAccess = security.OpenAccess,
});

app.MapGet("/config.js", () => Results.Text(
    $"window.__tennisturnier = {oberflaechenKonfiguration};",
    "application/javascript",
    Encoding.UTF8))
    .WithTags("Oberfläche");

// „Wer bin ich, und was darf ich" — die Oberfläche muss entscheiden, welche
// Schaltfläche sie überhaupt zeigt. Ohne Anmeldung ist „niemand" die Antwort
// und kein Fehler.
app.MapGet("/api/me", async (IMeService service, CancellationToken ct) =>
    await service.GetAsync(ct) is { } me ? Results.Ok(me) : Results.NoContent())
    .WithTags("Benutzer");

app.MapTournamentEndpoints();
app.MapMatchEndpoints();
app.MapMembershipEndpoints();
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
}
