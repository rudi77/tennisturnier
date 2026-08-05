using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

public sealed class PhaseConfiguration : IEntityTypeConfiguration<Phase>
{
    public void Configure(EntityTypeBuilder<Phase> builder)
    {
        builder.ToTable("Phases");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.TournamentId).IsRequired();
        builder.Property(p => p.Ordinal).IsRequired();
        builder.Property(p => p.Format).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);

        builder.HasOne<Tournament>()
            .WithMany()
            .HasForeignKey(p => p.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Matches)
            .WithOne()
            .HasForeignKey(m => m.PhaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Matches)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        // Berechnete Sicht, keine Beziehung — sonst legt EF eine Schattenspalte an.
        builder.Ignore(p => p.Status);

        builder.HasIndex(p => new { p.TournamentId, p.Ordinal }).IsUnique();
    }
}

public sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.ToTable("Matches");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.TournamentId).IsRequired();
        builder.Property(m => m.PhaseId).IsRequired();
        builder.Property(m => m.Round).IsRequired();
        builder.Property(m => m.Position).IsRequired();
        builder.Property(m => m.Label).HasMaxLength(100);
        builder.Property(m => m.Group).HasMaxLength(20);
        builder.Property(m => m.Version).IsConcurrencyToken();

        ConfigureSide(builder, m => m.Side1, "Side1");
        ConfigureSide(builder, m => m.Side2, "Side2");

        // Das Ergebnis als eine JSON-Spalte (ADR-0006): es wird immer ganz
        // gelesen und nie serverseitig durchsucht.
        builder.Property(m => m.Score!)
            .HasColumnName("Score")
            .HasConversion(new ScoreConverter(), new ScoreComparer())
            .IsRequired(false);

        builder.HasIndex(m => m.TournamentId);
        builder.HasIndex(m => new { m.PhaseId, m.Round, m.Position });

        // Die Indizes auf Side1_EntryId und Side2_EntryId liegen in der Migration
        // MatchesAndAssignments: EF Core kann Indizes nicht über die Spalten eines
        // Komplextyps beschreiben. Sie sind trotzdem nötig — „alle Matches dieser
        // Meldung" ist die häufigste Frage im ganzen System.
    }

    /// <summary>
    /// Eine Match-Seite als zwei Spalten: die Herkunft in kompakter Textform und
    /// die aufgelöste Meldung als eigene, indizierte Spalte.
    ///
    /// Die Trennung folgt daraus, wie die Daten benutzt werden. „Welche Matches
    /// hat dieser Teilnehmer" ist die häufigste Frage im ganzen System und muss
    /// indizierbar bleiben — daher die eigene Spalte. Die Herkunft dagegen wird
    /// nie gesucht, sondern immer nur zusammen mit ihrer Phase geladen und dann
    /// im Speicher ausgewertet; für sie genügt eine kodierte Zeichenkette
    /// (ADR-0006: kein serverseitiges Durchsuchen zusammengesetzter Werte).
    /// </summary>
    private static void ConfigureSide(
        EntityTypeBuilder<Match> builder,
        System.Linq.Expressions.Expression<Func<Match, MatchSide?>> side,
        string prefix)
    {
        builder.ComplexProperty(side, s =>
        {
            s.Property(x => x.EntryId).HasColumnName($"{prefix}_EntryId");

            s.Property(x => x.Origin)
                .HasColumnName($"{prefix}_Origin")
                .HasConversion(new ParticipantRefConverter(), new ParticipantRefComparer())
                .HasMaxLength(120)
                .IsRequired();
        });
    }
}

public sealed class CourtAssignmentConfiguration : IEntityTypeConfiguration<CourtAssignment>
{
    public void Configure(EntityTypeBuilder<CourtAssignment> builder)
    {
        builder.ToTable("CourtAssignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.TournamentId).IsRequired();
        builder.Property(a => a.MatchId).IsRequired();
        builder.Property(a => a.CourtId).IsRequired();
        builder.Property(a => a.SequenceOnCourt).IsRequired();
        builder.Property(a => a.EstimatedDuration).IsRequired();
        builder.Property(a => a.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Version).IsConcurrencyToken();

        // Aus PlannedStart und EstimatedDuration berechnet, keine eigene Spalte.
        builder.Ignore(a => a.PlannedSlot);

        builder.HasOne<Match>()
            .WithMany()
            .HasForeignKey(a => a.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<TournamentCourt>()
            .WithMany()
            .HasForeignKey(a => a.CourtId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.TournamentId);
        builder.HasIndex(a => a.MatchId);
        builder.HasIndex(a => new { a.CourtId, a.SequenceOnCourt });
    }
}
