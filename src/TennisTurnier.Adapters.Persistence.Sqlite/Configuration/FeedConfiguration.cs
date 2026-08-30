using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TennisTurnier.Domain.Social;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

/// <summary>
/// Der Feed eines Turniers (ADR-0014).
///
/// Fremdschlüssel auf das Turnier mit Kaskade: ein Eintrag ohne sein Turnier
/// hat keinen Sinn, und ein gelöschtes Turnier lässt nichts zurück — dieselbe
/// Regel wie bei Plätzen und Meldungen.
///
/// Kein Fremdschlüssel auf das Konto des Verfassers. Er zöge die Konten in jede
/// Feed-Abfrage, und die Namen werden ohnehin in einem Zug nachgeschlagen;
/// wichtiger: ein gelöschtes Konto soll den Verlauf nicht mitnehmen.
/// </summary>
public sealed class TournamentPostConfiguration : IEntityTypeConfiguration<TournamentPost>
{
    public void Configure(EntityTypeBuilder<TournamentPost> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TournamentPosts");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.TournamentId).IsRequired();
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.AuthorUserId);
        builder.Property(p => p.MatchId);
        builder.Property(p => p.Text).IsRequired().HasMaxLength(TournamentPost.MaxTextLength);
        builder.Property(p => p.CreatedAt).IsRequired();

        builder.Ignore(p => p.IsMessage);

        builder.HasOne<Tournament>()
            .WithMany()
            .HasForeignKey(p => p.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Comments)
            .WithOne()
            .HasForeignKey(c => c.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        // Die Kommentare gehören zum Eintrag und werden nie ohne ihn gelesen.
        builder.Navigation(p => p.Comments)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        // Die eine Abfrage, die es gibt: „die jüngsten Einträge dieses
        // Turniers".
        builder.HasIndex(p => new { p.TournamentId, p.CreatedAt });
    }
}

public sealed class PostCommentConfiguration : IEntityTypeConfiguration<PostComment>
{
    public void Configure(EntityTypeBuilder<PostComment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PostComments");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.PostId).IsRequired();
        builder.Property(c => c.AuthorUserId).IsRequired();
        builder.Property(c => c.Text).IsRequired().HasMaxLength(TournamentPost.MaxTextLength);
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasIndex(c => new { c.PostId, c.CreatedAt });
    }
}
