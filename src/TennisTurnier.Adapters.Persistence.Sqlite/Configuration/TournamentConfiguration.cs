using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TennisTurnier.Domain.Common;
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

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.SchedulingMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Discipline).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.TeamFormation).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.FormatTemplateId).IsRequired();

        // Der Ort als Spalten am Turnier, nicht als eigene Tabelle: ein Ort ohne
        // Turnier hat keine Bedeutung, und zwei Turniere an derselben Anlage
        // teilen sich nichts (siehe Venue).
        builder.ComplexProperty(t => t.Venue, venue =>
        {
            venue.Property(v => v.Name).HasColumnName("VenueName").IsRequired().HasMaxLength(200);
            venue.Property(v => v.Address).HasColumnName("VenueAddress").HasMaxLength(300);
            venue.Property(v => v.City).HasColumnName("VenueCity").HasMaxLength(200);
            venue.Property(v => v.TimeZoneId).HasColumnName("TimeZoneId").IsRequired().HasMaxLength(100);
        });

        // Als Owned Entity und nicht als ComplexProperty: nur so lässt sich der
        // eindeutige Index auf das Token legen. Er ist keine Formsache — das
        // Token ist das einzige Feld dieser Tabelle, das ohne Query-Filter
        // nachgeschlagen wird, und der Weg der Selbstmeldung führt über ihn.
        builder.OwnsOne(t => t.Registration, link =>
        {
            link.Property(r => r.Token).HasColumnName("RegistrationToken").IsRequired().HasMaxLength(64);
            link.Property(r => r.Capacity).HasColumnName("RegistrationCapacity");
            link.Property(r => r.Deadline).HasColumnName("RegistrationDeadline");

            link.HasIndex(r => r.Token).IsUnique();
        });

        builder.Navigation(t => t.Registration).IsRequired();

        // Der eingefrorene Snapshot als eine JSON-Spalte (ADR-0001, ADR-0006).
        // Die Spalte ist optional; EF ruft den Konverter bei null nicht auf,
        // weshalb er selbst nicht mit null umgehen muss.
        builder.Property(t => t.Format!)
            .HasColumnName("FormatSnapshot")
            .HasConversion(new FormatSnapshotConverter(), new FormatSnapshotComparer())
            .IsRequired(false);

        // Das Satzformat des Turniers, solange nicht ausgelost ist. Danach
        // steht es im Snapshot — die Spalte bleibt stehen und ist der Stand,
        // aus dem er hervorging.
        builder.Property(t => t.MatchFormat!)
            .HasColumnName("MatchFormat")
            .HasConversion(new MatchFormatConverter(), new MatchFormatComparer())
            .IsRequired(false);

        // Optimistische Nebenläufigkeit über einen fachlich gepflegten Zähler
        // statt rowversion — verhält sich auf SQLite und PostgreSQL gleich.
        builder.Property(t => t.Version).IsConcurrencyToken();

        builder.HasMany(t => t.Courts)
            .WithOne()
            .HasForeignKey(c => c.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Courts)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

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
        builder.Ignore(t => t.UnpairedEntries);
        builder.Ignore(t => t.FormedTeams);

        builder.HasIndex(t => t.StartsOn);
    }
}

public sealed class TournamentCourtConfiguration : IEntityTypeConfiguration<TournamentCourt>
{
    public void Configure(EntityTypeBuilder<TournamentCourt> builder)
    {
        builder.ToTable("TournamentCourts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TournamentId).IsRequired();
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Surface).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Location).HasConversion<string>().HasMaxLength(20);

        builder.HasMany(c => c.Windows)
            .WithOne()
            .HasForeignKey(w => w.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Windows)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        // Die Eindeutigkeit prüft bereits das Aggregat — hier steht sie ein
        // zweites Mal, weil sie sonst nur so lange hielte, wie jeder Weg über
        // das Aggregat führt.
        builder.HasIndex(c => new { c.TournamentId, c.Name }).IsUnique();
    }
}

public sealed class CourtWindowConfiguration : IEntityTypeConfiguration<CourtWindow>
{
    public void Configure(EntityTypeBuilder<CourtWindow> builder)
    {
        builder.ToTable("CourtWindows");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.TournamentId).IsRequired();
        builder.Property(w => w.CourtId).IsRequired();

        builder.ComplexProperty(w => w.Period, period =>
        {
            period.Property(p => p.Start).HasColumnName("From").IsRequired();
            period.Property(p => p.End).HasColumnName("To").IsRequired();
        });

        builder.HasIndex(w => new { w.TournamentId, w.CourtId });
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
        builder.Property(e => e.Origin).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.RegisteredAt).IsRequired();
        builder.Property(e => e.ConfirmationCode).IsRequired().HasMaxLength(32);

        // Kein Fremdschlüssel auf die Meldung des Teams: er zeigte innerhalb
        // derselben Tabelle auf eine Zeile desselben Aggregats, und EF Core
        // müsste beim Auflösen eines Teams eine Reihenfolge einhalten, die es
        // nicht kennt. Die Zuordnung setzt und löst ohnehin nur das Aggregat.
        builder.Property(e => e.TeamEntryId);
        builder.HasIndex(e => e.TeamEntryId);

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

        builder.Property(t => t.OwnerUserId);

        // Wie beim Turnier: ohne Token gingen zwei gleichzeitige Bearbeitungen
        // derselben Vorlage beide durch, schrieben dieselbe neue Version und
        // eine Änderung wäre stillschweigend verloren. Ein Turnier, das mit
        // dieser Version einfriert, wiese dann einen Stand aus, den es nie gab.
        builder.Property(t => t.Version).IsRequired().IsConcurrencyToken();

        builder.Property(t => t.Definition)
            .HasColumnName("Definition")
            .HasConversion(new FormatDefinitionConverter(), new FormatDefinitionComparer())
            .IsRequired();

        builder.HasIndex(t => t.OwnerUserId);
    }
}
