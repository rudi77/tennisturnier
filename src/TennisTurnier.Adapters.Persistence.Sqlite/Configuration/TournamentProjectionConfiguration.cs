using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TennisTurnier.Domain.PublicView;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

/// <summary>
/// Die öffentliche Projektion (ADR-0003).
///
/// Ohne Fremdschlüssel auf das Turnier, obwohl die Id dieselbe ist: die
/// Projektion überdauert bewusst nichts, was gelöscht wird, aber sie soll auch
/// nicht durch eine Kaskade verschwinden, die jemand anderswo einrichtet. Sie
/// wird ausdrücklich neu gebaut oder ausdrücklich entfernt.
/// </summary>
public sealed class TournamentProjectionConfiguration : IEntityTypeConfiguration<TournamentProjection>
{
    public void Configure(EntityTypeBuilder<TournamentProjection> builder)
    {
        builder.ToTable("TournamentProjections");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("TournamentId");
        builder.Property(p => p.Json).IsRequired();
        builder.Property(p => p.ETag).IsRequired().HasMaxLength(64);
        builder.Property(p => p.UpdatedAt).IsRequired();
        builder.Property(p => p.Version).IsConcurrencyToken();
    }
}
