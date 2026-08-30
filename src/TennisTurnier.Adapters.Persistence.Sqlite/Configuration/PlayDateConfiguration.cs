using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TennisTurnier.Domain.Social;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

/// <summary>
/// Die Verabredung (ADR-0015).
///
/// Kein Fremdschlüssel auf ein Turnier — sie hat keines. Und keiner auf die
/// Konten: die Einladungen sind Grundlage der Sichtbarkeit und dürfen nicht in
/// deren Abfrage geraten, aus demselben Grund, aus dem die Rollenzuweisungen
/// keinen tragen.
/// </summary>
public sealed class PlayDateConfiguration : IEntityTypeConfiguration<PlayDate>
{
    public void Configure(EntityTypeBuilder<PlayDate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PlayDates");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.HostUserId).IsRequired();
        builder.Property(d => d.Title).IsRequired().HasMaxLength(PlayDate.MaxTitleLength);
        builder.Property(d => d.Discipline).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.VenueName).IsRequired().HasMaxLength(PlayDate.MaxVenueLength);
        builder.Property(d => d.StartsAt).IsRequired();
        builder.Property(d => d.Duration).IsRequired();
        builder.Property(d => d.Note).HasMaxLength(PlayDate.MaxNoteLength);
        builder.Property(d => d.CreatedAt).IsRequired();
        builder.Property(d => d.IsCancelled).IsRequired();

        // Alles daran ist gerechnet und keine Spalte — der Zustand wird nicht
        // gepflegt, sondern abgeleitet (ADR-0015).
        builder.Ignore(d => d.RequiredPlayers);
        builder.Ignore(d => d.Committed);
        builder.Ignore(d => d.Missing);
        builder.Ignore(d => d.IsConfirmed);
        builder.Ignore(d => d.EndsAt);

        builder.HasMany(d => d.Invitations)
            .WithOne()
            .HasForeignKey(i => i.PlayDateId)
            .OnDelete(DeleteBehavior.Cascade);

        // Immer mitgeladen: ohne die Einladungen lässt sich weder die
        // Sichtbarkeit noch der Zustand beantworten.
        builder.Navigation(d => d.Invitations)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        builder.HasIndex(d => d.HostUserId);
        builder.HasIndex(d => d.StartsAt);
    }
}

public sealed class PlayDateInvitationConfiguration : IEntityTypeConfiguration<PlayDateInvitation>
{
    public void Configure(EntityTypeBuilder<PlayDateInvitation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PlayDateInvitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.PlayDateId).IsRequired();
        builder.Property(i => i.UserId).IsRequired();
        builder.Property(i => i.PlayerId).IsRequired();
        builder.Property(i => i.Response).HasConversion<string>().HasMaxLength(20);

        // Der Weg des Query-Filters: „welche Verabredungen betreffen mich".
        builder.HasIndex(i => i.UserId);

        // Dieselbe Person zweimal einzuladen ist derselbe Klick und keine
        // zweite Einladung — das Aggregat weist es ab, und der Index hält es
        // auch dann, wenn zwei Anfragen gleichzeitig hereinkommen.
        builder.HasIndex(i => new { i.PlayDateId, i.UserId }).IsUnique();
    }
}
