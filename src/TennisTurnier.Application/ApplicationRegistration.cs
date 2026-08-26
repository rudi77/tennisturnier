using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Application.Membership;
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
    /// <param name="tournamentOptions">
    /// Was für alle Turniere dieser Instanz gilt. Ohne Angabe die Vorgaben —
    /// unter anderem echter Zufall beim Los der Teams.
    /// </param>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        BootstrapAdminOptions? bootstrapAdmins = null,
        TournamentOptions? tournamentOptions = null)
    {
        services.AddSingleton(bootstrapAdmins ?? new BootstrapAdminOptions());
        services.AddSingleton(tournamentOptions ?? new TournamentOptions());
        services.AddScoped<SystemAdminBootstrap>();
        services.AddScoped<OrganizerBootstrap>();

        services.AddScoped<IMeService, MeService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IPostCommitQueue, PostCommitQueue>();
        services.AddScoped<DrawBuilder>();
        services.AddScoped<ParticipantResolver>();
        services.AddScoped<ITournamentService, TournamentService>();
        services.AddScoped<IMatchService, MatchService>();
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IFormatTemplateService, FormatTemplateService>();
        services.AddScoped<IPublicViewService, PublicViewService>();
        services.AddScoped<ISchedulingService, SchedulingService>();
        services.AddScoped<ICourtQueueService, CourtQueueService>();
        services.AddScoped<IJoinService, JoinService>();
        services.AddScoped<IEntryImportService, EntryImportService>();
        services.AddScoped<ITeamFormationService, TeamFormationService>();

        return services;
    }
}
