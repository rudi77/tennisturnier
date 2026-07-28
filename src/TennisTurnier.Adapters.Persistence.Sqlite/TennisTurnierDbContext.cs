using Microsoft.EntityFrameworkCore;
using TennisTurnier.Adapters.Persistence.Sqlite.Configuration;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Tournaments;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Sqlite;

public sealed class TennisTurnierDbContext : DbContext
{
    private readonly IUserContext _userContext;

    public TennisTurnierDbContext(DbContextOptions<TennisTurnierDbContext> options, IUserContext userContext)
        : base(options)
    {
        _userContext = userContext;
    }

    public DbSet<Club> Clubs => Set<Club>();

    public DbSet<Court> Courts => Set<Court>();

    public DbSet<Tournament> Tournaments => Set<Tournament>();

    public DbSet<Participant> Participants => Set<Participant>();

    public DbSet<Player> Players => Set<Player>();

    public DbSet<FormatTemplate> FormatTemplates => Set<FormatTemplate>();

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    /// <summary>
    /// Sieht der Aufrufer alle Vereine? Wird vom Query-Filter ausgewertet.
    ///
    /// Der Zugriff läuft absichtlich über Eigenschaften des DbContext und nicht
    /// über konstante Werte: EF Core übersetzt sie in Query-Parameter, sodass der
    /// zwischengespeicherte Abfrageplan für jeden Benutzer gültig bleibt.
    /// </summary>
    private bool SeesAllClubs => _userContext.Current.IsSystemAdmin;

    private IReadOnlyCollection<Guid> VisibleClubIds => _userContext.Current.ClubIds;

    /// <summary>
    /// Turniere, zu denen der Aufrufer eine turniergebundene Rolle hat. Ohne sie
    /// sähe ein Turnierleiter ohne Vereinsrolle sein eigenes Turnier nicht.
    /// </summary>
    private IReadOnlyCollection<Guid> VisibleTournamentIds => _userContext.Current.TournamentIds;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Gilt für jede Zeitangabe im Modell — siehe UtcDateTimeOffsetConverter.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();

        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TennisTurnierDbContext).Assembly);

        // ADR-0004: Der Query-Filter ist die eigentliche Sicherheitsgrenze. Eine
        // vergessene Prüfung im Endpunkt darf keine fremden Vereinsdaten
        // ausliefern können.
        modelBuilder.Entity<Club>()
            .HasQueryFilter(club => SeesAllClubs || VisibleClubIds.Contains(club.Id));

        modelBuilder.Entity<Court>()
            .HasQueryFilter(court => SeesAllClubs || VisibleClubIds.Contains(court.ClubId));

        // Auch die Kindtabellen brauchen den Filter. Über die Navigation vom
        // Verein aus wären sie zwar bereits abgeschirmt, aber ADR-0004 begründet
        // den Filter gerade damit, dass er nicht davon abhängt, auf welchem Weg
        // jemand abfragt. Ein direktes `Set<CourtBlock>()` lieferte sonst die
        // internen Notizen fremder Vereine.
        modelBuilder.Entity<AvailabilityWindow>()
            .HasQueryFilter(window => SeesAllClubs || Courts.Any(
                court => court.Id == window.CourtId && VisibleClubIds.Contains(court.ClubId)));

        modelBuilder.Entity<CourtBlock>()
            .HasQueryFilter(block => SeesAllClubs || Courts.Any(
                court => court.Id == block.CourtId && VisibleClubIds.Contains(court.ClubId)));

        // Zwei Wege führen zu einem Turnier: über den ausrichtenden Verein oder
        // über eine turniergebundene Rolle. Beide gehören in den Filter, sonst
        // ist die Rolle TournamentDirector wirkungslos.
        modelBuilder.Entity<Tournament>()
            .HasQueryFilter(tournament =>
                SeesAllClubs
                || VisibleClubIds.Contains(tournament.ClubId)
                || VisibleTournamentIds.Contains(tournament.Id));

        modelBuilder.Entity<TournamentEntry>()
            .HasQueryFilter(entry =>
                SeesAllClubs
                || VisibleTournamentIds.Contains(entry.TournamentId)
                || Tournaments.Any(tournament =>
                    tournament.Id == entry.TournamentId && VisibleClubIds.Contains(tournament.ClubId)));

        // Vorlagen ohne Verein sind die mitgelieferten Standardformate und für
        // jeden sichtbar; eigene Vorlagen bleiben im Verein.
        modelBuilder.Entity<FormatTemplate>()
            .HasQueryFilter(template =>
                SeesAllClubs
                || template.ClubId == null
                || VisibleClubIds.Contains(template.ClubId.Value));

        // Spieler und Teilnehmer tragen bewusst keine ClubId (ADR-0008) und
        // können daher nicht gefiltert werden. Ihr Schutz entsteht dort, wo
        // Kontaktdaten abgebildet werden, nicht hier.

        UseDomainAssignedKeys(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Alle Schlüssel vergibt die Domäne, nicht die Datenbank.
    ///
    /// Ohne diese Angabe hält EF Core einen Guid-Schlüssel für generiert und
    /// schließt aus einem bereits gesetzten Wert, die Entität sei bereits
    /// gespeichert. Ein neu über <c>Club.AddCourt</c> angelegter Platz würde dann
    /// als UPDATE geschrieben, das keine Zeile trifft — und der Platz wäre
    /// stillschweigend nicht angelegt. Es lohnt, das zentral zu setzen statt
    /// je Entität daran zu denken.
    /// </summary>
    private static void UseDomainAssignedKeys(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.FindPrimaryKey()?.Properties ?? [])
            {
                property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }
}
