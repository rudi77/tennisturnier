using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Application.Clubs;

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

        return services;
    }
}
