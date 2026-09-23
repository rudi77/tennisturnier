using System.Text.Json.Serialization;
using Matchday.Domain;
using Matchday.Server.Agent;
using Matchday.Server.Api;
using Matchday.Server.Auth;
using Matchday.Server.Live;
using Matchday.Server.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Die Vorgabe steht in appsettings.json, nicht hier ein zweites Mal. Fehlt sie,
// soll es beim Start auffallen und nicht in einer leeren Datenbank enden.
var connectionString = builder.Configuration.GetConnectionString("Default");
ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
builder.Services.AddSingleton(new TournamentStore(connectionString));
builder.Services.AddSingleton<LiveHub>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TournamentActions>();
builder.Services.AddSingleton<AgentTools>();
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));

// --- Die Anmeldung ---------------------------------------------------------
//
// Ein Schalter, kein Umbau: Steht Auth:Required auf false, wird hier gar nichts
// eingehängt, und die Anwendung verhält sich wie in ADR-0016 beschrieben.
// Steht er auf true, prüft ASP.NET die Id-Token von Google selbst — Signatur
// gegen Googles Schlüssel, Aussteller, Audience und Ablauf. Eigene Krypto
// wäre hier genau die falsche Stelle für Selbstgebautes.
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

var auth = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();

// Verlangen ohne Client-Id hieße: jeden abweisen, weil kein Token je gültig
// sein kann. Lieber beim Start abbrechen als im Betrieb jeden aussperren.
if (!auth.IsConfigured)
{
    throw new InvalidOperationException(
        "Auth__Required ist gesetzt, aber Auth__GoogleClientId fehlt. Ohne Client-Id kann kein Token gelten.");
}

if (auth.Required)
{
    // Die Schlüssel, mit denen das Sitzungs-Cookie verschlüsselt ist. Lägen sie
    // im Container, wäre nach jedem Deploy jede Sitzung ungültig — sie gehören
    // auf den Datenträger neben die Datenbank.
    var protection = builder.Services.AddDataProtection().SetApplicationName("matchday");

    if (!string.IsNullOrWhiteSpace(auth.KeysPath))
    {
        protection.PersistKeysToFileSystem(new DirectoryInfo(auth.KeysPath));
    }

    // Zwei Wege herein, ein Schema davor: Das Google-Token kommt genau einmal,
    // beim Einlösen gegen die Sitzung; danach trägt das Cookie (ADR-0025).
    builder.Services
        .AddAuthentication(AuthOptions.Scheme)
        .AddPolicyScheme(AuthOptions.Scheme, "Sitzung oder Google", options =>
            options.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal)
                    ? JwtBearerDefaults.AuthenticationScheme
                    : CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.Cookie.Name = AuthEndpoint.CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
        })
        .AddJwtBearer(options =>
        {
            options.Authority = AuthOptions.GoogleIssuer;
            options.Audience = auth.GoogleClientId;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = AuthOptions.GoogleIssuer,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
            };
        });

    builder.Services.AddAuthorization();
}
builder.Services.AddSingleton<ModelAccess>();
builder.Services.AddSingleton<TournamentAgent>();

builder.Services.AddSingleton(new IndexPage(builder.Environment.WebRootFileProvider));

var app = builder.Build();

// Railway schließt TLS vor der Anwendung ab. Ohne die weitergereichten
// Kopfzeilen hielte sie jede Anfrage für http und gäbe das Cookie ohne
// Secure heraus.
var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost };
forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, message) = exception switch
    {
        DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Message),
        NotFoundException e => (StatusCodes.Status404NotFound, e.Message),
        UnauthorizedException e => (StatusCodes.Status401Unauthorized, e.Message),
        ForbiddenException e => (StatusCodes.Status403Forbidden, e.Message),
        ConflictException e => (StatusCodes.Status409Conflict, e.Message),
        BadHttpRequestException e => (StatusCodes.Status400BadRequest, e.Message),
        _ => (StatusCodes.Status500InternalServerError, "Da ist etwas schiefgegangen."),
    };

    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { error = message });
}));

if (auth.Required)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapAuth();
app.MapMatchday();
app.MapChat();

// Die Oberfläche liegt gebaut neben der Anwendung; alles, was keine API ist, ist die eine Seite.
// Die Seite selbst geht durch die Vorschau: Ein geteilter Link soll in der
// Gruppe zeigen, um welches Turnier es geht.
app.UsePreview();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Für die Api-Tests sichtbar.</summary>
public partial class Program;
