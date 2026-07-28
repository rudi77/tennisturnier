using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Application.Tournaments;

namespace TennisTurnier.Application;

public static class ApplicationRegistration
{
    /// <summary>
    /// Registriert die Anwendungsfälle. Die Driven Ports kommen aus den Adaptern —
    /// diese Schicht kennt keine Implementierung davon.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IClubService, ClubService>();
        services.AddScoped<DrawBuilder>();
        services.AddScoped<ITournamentService, TournamentService>();
        services.AddScoped<IMatchService, MatchService>();
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IFormatTemplateService, FormatTemplateService>();
        services.AddScoped<IPublicViewService, PublicViewService>();

        return services;
    }
}
