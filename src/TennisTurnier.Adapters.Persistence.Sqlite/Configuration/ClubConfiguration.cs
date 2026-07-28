using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TennisTurnier.Domain.Clubs;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

/// <summary>
/// Die Zuordnung liegt hier und nicht als Attribut an der Entität — der
/// Domänenkern kennt EF Core nicht (ADR-0005).
/// </summary>
public sealed class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    public void Configure(EntityTypeBuilder<Club> builder)
    {
        builder.ToTable("Clubs");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.City).HasMaxLength(200);
        builder.Property(c => c.TimeZoneId).IsRequired().HasMaxLength(100);

        builder.HasMany(c => c.Courts)
            .WithOne()
            .HasForeignKey(court => court.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Courts)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        builder.HasIndex(c => c.Name);
    }
}
