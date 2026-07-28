using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Application.Ports;

namespace TennisTurnier.Adapters.Scheduling;

/// <summary>Ankertyp, über den Tests und Composition Root die Assembly finden.</summary>
public sealed class SchedulingAssembly
{
    private SchedulingAssembly() { }
}

public static class SchedulingRegistration
{
    /// <summary>
    /// Registriert den Spielplan-Solver. Er ist zustandslos und deshalb ein
    /// Singleton — und er ist hinter dem Port austauschbar, falls einmal ein
    /// anderes Verfahren gebraucht wird (ADR-0002).
    /// </summary>
    public static IServiceCollection AddHeuristicScheduling(this IServiceCollection services)
    {
        services.AddSingleton<IScheduleSolver, HeuristicScheduleSolver>();

        return services;
    }
}
