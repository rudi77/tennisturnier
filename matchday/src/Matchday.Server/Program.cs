using System.Text.Json.Serialization;
using Matchday.Domain;
using Matchday.Server.Agent;
using Matchday.Server.Api;
using Matchday.Server.Auth;
using Matchday.Server.Live;
using Matchday.Server.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, message) = exception switch
    {
        DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Message),
        NotFoundException e => (StatusCodes.Status404NotFound, e.Message),
        UnauthorizedException e => (StatusCodes.Status401Unauthorized, e.Message),
        ForbiddenException e => (StatusCodes.Status403Forbidden, e.Message),
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
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Für die Api-Tests sichtbar.</summary>
public partial class Program;
