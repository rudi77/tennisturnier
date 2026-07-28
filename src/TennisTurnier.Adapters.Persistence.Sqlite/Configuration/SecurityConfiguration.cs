using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

public sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("UserAccounts");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Issuer).IsRequired().HasMaxLength(300);
        builder.Property(u => u.SubjectId).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.Property(u => u.DisplayName).HasMaxLength(200);

        // Der Aussteller gehört in den Schlüssel: ein „sub" ist nur innerhalb
        // seines Identity Providers eindeutig (ADR-0007).
        builder.HasIndex(u => new { u.Issuer, u.SubjectId }).IsUnique();
    }
}

public sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.ToTable("RoleAssignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.Role).HasConversion<string>().HasMaxLength(30);

        builder.ComplexProperty(a => a.Scope, scope =>
        {
            scope.Property(s => s.Type).HasColumnName("ScopeType").HasConversion<string>().HasMaxLength(20);
            scope.Property(s => s.ResourceId).HasColumnName("ScopeResourceId");
        });

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.UserId);

        // Der eindeutige Index über (UserId, Role, ScopeType, ScopeResourceId)
        // liegt in der Migration UniqueRoleAssignment und nicht hier: EF Core
        // kann Indizes nicht über die Spalten eines Komplextyps beschreiben.
    }
}
