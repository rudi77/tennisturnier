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

/// <summary>
/// Die Einladung, die auf ihr Konto wartet.
///
/// Kein Fremdschlüssel auf ein Konto — es gibt ja gerade keines. Und keiner
/// auf das Turnier: die Einladung ist wie die Rollenzuweisung Grundlage der
/// Sichtbarkeit und darf nicht in deren Query-Filter geraten.
/// </summary>
public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Invitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.TournamentId).IsRequired();
        builder.Property(i => i.Email).IsRequired().HasMaxLength(320);
        builder.Property(i => i.Role).HasConversion<string>().HasMaxLength(30);
        builder.Property(i => i.CreatedAt).IsRequired();

        // Der Weg beim ersten Login geht über die Adresse allein.
        builder.HasIndex(i => i.Email);

        // Dieselbe Rolle zweimal an dieselbe Adresse ist keine zweite
        // Einladung, sondern der zweite Klick auf dieselbe Schaltfläche.
        builder.HasIndex(i => new { i.TournamentId, i.Email, i.Role }).IsUnique();
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
        // kann Indizes nicht über die Spalten eines Komplextyps beschreiben —
        // und <c>Scope</c> ist einer.
    }
}
