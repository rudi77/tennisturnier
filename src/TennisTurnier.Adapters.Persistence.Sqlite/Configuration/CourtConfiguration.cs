using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TennisTurnier.Domain.Clubs;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

public sealed class CourtConfiguration : IEntityTypeConfiguration<Court>
{
    public void Configure(EntityTypeBuilder<Court> builder)
    {
        builder.ToTable("Courts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ClubId).IsRequired();
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Surface).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.Location).HasConversion<string>().HasMaxLength(30);

        builder.HasMany(c => c.Availability)
            .WithOne()
            .HasForeignKey(w => w.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Blocks)
            .WithOne()
            .HasForeignKey(b => b.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Availability)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        builder.Navigation(c => c.Blocks)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        builder.HasIndex(c => new { c.ClubId, c.Name }).IsUnique();
    }
}

public sealed class AvailabilityWindowConfiguration : IEntityTypeConfiguration<AvailabilityWindow>
{
    public void Configure(EntityTypeBuilder<AvailabilityWindow> builder)
    {
        builder.ToTable("AvailabilityWindows");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.CourtId).IsRequired();
        builder.Property(w => w.DayOfWeek).HasConversion<string>().HasMaxLength(10);
        builder.Property(w => w.OpensAt).IsRequired();
        builder.Property(w => w.ClosesAt).IsRequired();
        builder.Property(w => w.ValidFrom).IsRequired();

        builder.HasIndex(w => new { w.CourtId, w.DayOfWeek });
    }
}

public sealed class CourtBlockConfiguration : IEntityTypeConfiguration<CourtBlock>
{
    public void Configure(EntityTypeBuilder<CourtBlock> builder)
    {
        builder.ToTable("CourtBlocks");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.CourtId).IsRequired();
        builder.Property(b => b.Reason).HasConversion<string>().HasMaxLength(30);
        builder.Property(b => b.Note).HasMaxLength(500);

        // TimeSlot ist ein Wertobjekt ohne eigene Identität und wird als zwei
        // Spalten der Sperre abgelegt.
        builder.ComplexProperty(b => b.Period, period =>
        {
            period.Property(p => p.Start).HasColumnName("StartsAt").IsRequired();
            period.Property(p => p.End).HasColumnName("EndsAt").IsRequired();
        });

        builder.HasIndex(b => b.CourtId);
    }
}
