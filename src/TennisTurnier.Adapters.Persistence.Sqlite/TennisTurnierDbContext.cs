using Microsoft.EntityFrameworkCore;
using TennisTurnier.Adapters.Persistence.Sqlite.Configuration;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.PublicView;
using TennisTurnier.Domain.Scheduling;
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

    public DbSet<TournamentCourt> Courts => Set<TournamentCourt>();

    public DbSet<Tournament> Tournaments => Set<Tournament>();

    public DbSet<Participant> Participants => Set<Participant>();

    public DbSet<Player> Players => Set<Player>();

    public DbSet<Phase> Phases => Set<Phase>();

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<CourtAssignment> CourtAssignments => Set<CourtAssignment>();

    public DbSet<FormatTemplate> FormatTemplates => Set<FormatTemplate>();

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    /// <summary>
    /// Die öffentliche Projektion (ADR-0003). Bewusst ohne Query-Filter — sie
    /// wird von Zuschauern ohne Anmeldung gelesen.
    /// </summary>
    public DbSet<TournamentProjection> TournamentProjections => Set<TournamentProjection>();

    /// <summary>
    /// Sieht der Aufrufer alles? Wird vom Query-Filter ausgewertet.
    ///
    /// Der Zugriff läuft absichtlich über Eigenschaften des DbContext und nicht
    /// über konstante Werte: EF Core übersetzt sie in Query-Parameter, sodass der
    /// zwischengespeicherte Abfrageplan für jeden Benutzer gültig bleibt.
    /// </summary>
    private bool SeesEverything => _userContext.Current.IsSystemAdmin;

    /// <summary>
    /// Die Turniere, zu denen der Aufrufer eine Rolle hat — seit dem Wegfall
    /// des Vereins der einzige Sichtbarkeitsschlüssel.
    /// </summary>
    private IReadOnlyCollection<Guid> VisibleTournamentIds => _userContext.Current.TournamentIds;

    /// <summary>Der Aufrufer selbst — die eigenen Formatvorlagen hängen daran.</summary>
    private Guid CallerId => _userContext.Current.UserId;

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
        // vergessene Prüfung im Endpunkt darf keine fremden Daten ausliefern
        // können.
        //
        // Er hing bis vor Kurzem an den Vereinen des Aufrufers. Jetzt hängt er an
        // seinen Turnieren, und das ist die engere Grenze: wer keine Rolle an
        // einem Turnier hat, sieht es nicht.
        //
        // Ein Turnier sieht, wer eine Rolle daran hat. Das ist der ganze Filter
        // — und die Stelle, von der alle anderen abhängen.
        modelBuilder.Entity<Tournament>()
            .HasQueryFilter(tournament =>
                SeesEverything || VisibleTournamentIds.Contains(tournament.Id));

        // Plätze, Platzzeiten und Meldungen prüfen einstufig gegen die sichtbaren
        // Turniere statt über eine Unterabfrage — dasselbe Muster, das Phase,
        // Match und Platzzuweisung bereits benutzen. Sie tragen dafür alle eine
        // eigene Turnierkennung, auch wenn sie über die Navigation erreichbar
        // wären: ADR-0004 begründet den Filter gerade damit, dass er nicht davon
        // abhängt, auf welchem Weg jemand abfragt.
        modelBuilder.Entity<TournamentCourt>()
            .HasQueryFilter(court =>
                SeesEverything || VisibleTournamentIds.Contains(court.TournamentId));

        modelBuilder.Entity<CourtWindow>()
            .HasQueryFilter(window =>
                SeesEverything || VisibleTournamentIds.Contains(window.TournamentId));

        modelBuilder.Entity<TournamentEntry>()
            .HasQueryFilter(entry =>
                SeesEverything || VisibleTournamentIds.Contains(entry.TournamentId));

        // Vorlagen ohne Eigentümer sind die mitgelieferten Standardformate und
        // für jeden sichtbar; eine eigene Vorlage sieht nur, wem sie gehört.
        modelBuilder.Entity<FormatTemplate>()
            .HasQueryFilter(template =>
                SeesEverything
                || template.OwnerUserId == null
                || template.OwnerUserId == CallerId);

        // Phasen, Matches und Platzzuweisungen hängen am Turnier und erben
        // dessen Sichtbarkeit.
        modelBuilder.Entity<Phase>()
            .HasQueryFilter(phase => SeesEverything || Tournaments.Any(t => t.Id == phase.TournamentId));

        modelBuilder.Entity<Match>()
            .HasQueryFilter(match => SeesEverything || Tournaments.Any(t => t.Id == match.TournamentId));

        modelBuilder.Entity<CourtAssignment>()
            .HasQueryFilter(a => SeesEverything || Tournaments.Any(t => t.Id == a.TournamentId));

        // Die öffentliche Projektion bekommt bewusst keinen Filter: sie enthält
        // nur, was jeder sehen darf, und wird ohne Anmeldung gelesen (ADR-0003).
        // Diese Ausnahme ist die Begründung dafür, dass es sie überhaupt gibt.

        // Spieler und Teilnehmer gehören keinem Turnier (ADR-0008) und können
        // daher nicht gefiltert werden. Ihr Schutz entsteht dort, wo
        // Kontaktdaten abgebildet werden, nicht hier.

        UseDomainAssignedKeys(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Alle Schlüssel vergibt die Domäne, nicht die Datenbank.
    ///
    /// Ohne diese Angabe hält EF Core einen Guid-Schlüssel für generiert und
    /// schließt aus einem bereits gesetzten Wert, die Entität sei bereits
    /// gespeichert. Ein neu über <c>Tournament.AddCourt</c> angelegter Platz würde dann
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
