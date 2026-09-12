using System.Text.Json.Serialization;
using Matchday.Domain;
using Matchday.Server.Agent;
using Matchday.Server.Api;
using Matchday.Server.Live;
using Matchday.Server.Storage;
using Microsoft.AspNetCore.Diagnostics;

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
        ForbiddenException e => (StatusCodes.Status403Forbidden, e.Message),
        BadHttpRequestException e => (StatusCodes.Status400BadRequest, e.Message),
        _ => (StatusCodes.Status500InternalServerError, "Da ist etwas schiefgegangen."),
    };

    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { error = message });
}));

app.MapMatchday();
app.MapChat();

// Die Oberfläche liegt gebaut neben der Anwendung; alles, was keine API ist, ist die eine Seite.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Für die Api-Tests sichtbar.</summary>
public partial class Program;
