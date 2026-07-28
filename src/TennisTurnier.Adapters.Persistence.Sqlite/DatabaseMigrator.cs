using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TennisTurnier.Adapters.Persistence.Sqlite;

public static class DatabaseMigrator
{
    /// <summary>
    /// Bringt das Schema auf den Stand der Migrationen. Bewusst ein eigener,
    /// einmal aufzurufender Schritt und kein Nebeneffekt des Anwendungsstarts:
    /// zwei gleichzeitig startende Prozesse würden einander sonst überholen.
    /// </summary>
    public static async Task MigrateDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TennisTurnierDbContext>();

        await db.Database.MigrateAsync(cancellationToken);
    }
}
