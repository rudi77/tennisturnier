using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Sqlite;

/// <summary>
/// Nur für <c>dotnet ef</c>. Migrationen beschreiben das Schema und sind vom
/// Query-Filter unabhängig — deshalb genügt hier der Systemkontext.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TennisTurnierDbContext>
{
    public TennisTurnierDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TennisTurnierDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new TennisTurnierDbContext(options, new SystemUserContext());
    }

    private sealed class SystemUserContext : IUserContext
    {
        public UserPrincipal Current => UserPrincipal.System;
    }
}
