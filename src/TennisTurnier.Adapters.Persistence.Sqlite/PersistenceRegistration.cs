using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Adapters.Persistence.Sqlite.Repositories;
using TennisTurnier.Application.Ports;

namespace TennisTurnier.Adapters.Persistence.Sqlite;

public static class PersistenceRegistration
{
    /// <summary>
    /// Registriert den SQLite-Adapter. Der Aufrufer — die Composition Root —
    /// entscheidet über die Verbindungszeichenfolge; der Adapter kennt keine
    /// Konfigurationsquelle.
    /// </summary>
    public static IServiceCollection AddSqlitePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<TennisTurnierDbContext>(options => options.UseSqlite(connectionString));

        services.AddScoped<IClubRepository, ClubRepository>();
        services.AddScoped<ITournamentRepository, TournamentRepository>();
        services.AddScoped<IFormatTemplateRepository, FormatTemplateRepository>();
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly TennisTurnierDbContext _db;

    public UnitOfWork(TennisTurnierDbContext db) => _db = db;

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
