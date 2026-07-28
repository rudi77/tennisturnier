using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

public sealed class TournamentConfiguration : IEntityTypeConfiguration<Tournament>
{
    public void Configure(EntityTypeBuilder<Tournament> builder)
    {
        builder.ToTable("Tournaments");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.ClubId).IsRequired();
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.SchedulingMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.FormatTemplateId).IsRequired();

        // Der eingefrorene Snapshot als eine JSON-Spalte (ADR-0001, ADR-0006).
        // Die Spalte ist optional; EF ruft den Konverter bei null nicht auf,
        // weshalb er selbst nicht mit null umgehen muss.
        builder.Property(t => t.Format!)
            .HasColumnName("FormatSnapshot")
            .HasConversion(new FormatSnapshotConverter(), new FormatSnapshotComparer())
            .IsRequired(false);

        // Optimistische Nebenläufigkeit über einen fachlich gepflegten Zähler
        // statt rowversion — verhält sich auf SQLite und PostgreSQL gleich.
        builder.Property(t => t.Version).IsConcurrencyToken();

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(t => t.ClubId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Entries)
            .WithOne()
            .HasForeignKey(e => e.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Entries)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        // AcceptedEntries ist eine berechnete Sicht auf Entries, keine eigene
        // Beziehung. Ohne dieses Ignorieren erkennt EF Core sie als zweite
        // Sammelnavigation und legt eine Schattenspalte TournamentId1 samt Index
        // an — ein Fremdschlüssel, der einem abgeleiteten Status folgt und beim
        // Rückzug wieder genullt wird.
        builder.Ignore(t => t.AcceptedEntries);

        builder.HasIndex(t => t.ClubId);
        builder.HasIndex(t => new { t.ClubId, t.StartsOn });
    }
}

public sealed class TournamentEntryConfiguration : IEntityTypeConfiguration<TournamentEntry>
{
    public void Configure(EntityTypeBuilder<TournamentEntry> builder)
    {
        builder.ToTable("TournamentEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TournamentId).IsRequired();
        builder.Property(e => e.ParticipantId).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Participant>()
            .WithMany()
            .HasForeignKey(e => e.ParticipantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Nachschlageindex für „welche Meldungen hat dieser Teilnehmer".
        // Bewusst nicht eindeutig: dass ein Teilnehmer nur eine offene Meldung
        // haben darf, ist eine Regel über den Status und steht im Aggregat —
        // eine zurückgezogene und eine neue Meldung sind zulässig.
        builder.HasIndex(e => new { e.TournamentId, e.ParticipantId });
    }
}

public sealed class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> builder)
    {
        builder.ToTable("Participants");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.DisplayName).IsRequired().HasMaxLength(200);

        // Ein oder zwei Spieler-Ids. Für eine eigene Tabelle zu wenig Struktur,
        // und abgefragt wird die Liste nie einzeln — geladen wird sie immer
        // ganz, um Überschneidungen im Spielplan zu prüfen.
        builder.Property<IReadOnlyList<Guid>>("PlayerIds")
            .HasField("_playerIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("PlayerIds")
            .HasConversion(new GuidListConverter(), new GuidListComparer())
            .IsRequired();
    }
}

public sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("Players");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(p => p.LastName).IsRequired().HasMaxLength(100);

        builder.ComplexProperty(p => p.Contact, contact =>
        {
            contact.Property(c => c.Email).HasColumnName("Email").HasMaxLength(320);
            contact.Property(c => c.Phone).HasColumnName("Phone").HasMaxLength(50);
            contact.Property(c => c.DateOfBirth).HasColumnName("DateOfBirth");
        });

        builder.HasIndex(p => new { p.LastName, p.FirstName });
    }
}

public sealed class FormatTemplateConfiguration : IEntityTypeConfiguration<FormatTemplate>
{
    public void Configure(EntityTypeBuilder<FormatTemplate> builder)
    {
        builder.ToTable("FormatTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.ClubId);

        // Wie beim Turnier: ohne Token gingen zwei gleichzeitige Bearbeitungen
        // derselben Vorlage beide durch, schrieben dieselbe neue Version und
        // eine Änderung wäre stillschweigend verloren. Ein Turnier, das mit
        // dieser Version einfriert, wiese dann einen Stand aus, den es nie gab.
        builder.Property(t => t.Version).IsRequired().IsConcurrencyToken();

        builder.Property(t => t.Definition)
            .HasColumnName("Definition")
            .HasConversion(new FormatDefinitionConverter(), new FormatDefinitionComparer())
            .IsRequired();

        builder.HasIndex(t => t.ClubId);
    }
}
