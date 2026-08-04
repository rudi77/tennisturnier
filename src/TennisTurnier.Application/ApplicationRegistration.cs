using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tournaments;

namespace TennisTurnier.Application;

public static class ApplicationRegistration
{
    /// <summary>
    /// Registriert die Anwendungsfälle. Die Driven Ports kommen aus den Adaptern —
    /// diese Schicht kennt keine Implementierung davon.
    /// </summary>
    /// <param name="bootstrapAdmins">
    /// Die vorab konfigurierten Systemadministratoren. Gebunden wird im
    /// Composition Root, damit diese Schicht keine Konfigurationsquelle kennen
    /// muss. Ohne Angabe gibt es keine — dann bleibt es bei den Rollen aus der
    /// Datenbank.
    /// </param>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        BootstrapAdminOptions? bootstrapAdmins = null)
    {
        services.AddSingleton(bootstrapAdmins ?? new BootstrapAdminOptions());
        services.AddScoped<SystemAdminBootstrap>();

        services.AddScoped<IPostCommitQueue, PostCommitQueue>();
        services.AddScoped<IClubService, ClubService>();
        services.AddScoped<DrawBuilder>();
        services.AddScoped<ITournamentService, TournamentService>();
        services.AddScoped<IMatchService, MatchService>();
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IFormatTemplateService, FormatTemplateService>();
        services.AddScoped<IPublicViewService, PublicViewService>();
        services.AddScoped<ISchedulingService, SchedulingService>();
        services.AddScoped<ICourtQueueService, CourtQueueService>();

        return services;
    }
}
