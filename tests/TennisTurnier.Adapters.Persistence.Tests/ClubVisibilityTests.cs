using Microsoft.EntityFrameworkCore;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Adapters.Persistence.Tests;

/// <summary>
/// Der Query-Filter ist nach ADR-0004 die eigentliche Sicherheitsgrenze — nicht
/// die Prüfung im Endpunkt. Diese Tests sind daher keine Nebensache: sie prüfen,
/// dass ein vergessener Autorisierungsaufruf keine fremden Daten ausliefern
/// kann.
///
/// Der Verein ist dabei keine Grenze mehr, sondern ein Anhängsel: sichtbar ist
/// er, solange ein sichtbares Turnier auf ihn zeigt — weil seine Plätze zum
/// Spielplan gebraucht werden. Wer kein Turnier hat, sieht auch keinen Verein.
/// </summary>
public sealed class ClubVisibilityTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();

    private Guid _clubA;
    private Guid _clubB;
    private Guid _tournamentInA;
    private Guid _tournamentInB;

    public async Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        await using var db = _database.NewContext();

        var a = new Club(Guid.NewGuid(), "TC Alpha", "Europe/Vienna");
        Equip(a.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor), "Nur für Verein A");

        var b = new Club(Guid.NewGuid(), "TC Beta", "Europe/Vienna");
        Equip(b.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Hard, CourtLocation.Indoor), "Nur für Verein B");

        var template = new FormatTemplate(Guid.NewGuid(), null, BuiltInFormats.Knockout);

        db.Clubs.AddRange(a, b);
        db.FormatTemplates.Add(template);

        var inA = NewTournament(a.Id, template.Id, "Turnier in Alpha");
        var inB = NewTournament(b.Id, template.Id, "Turnier in Beta");
        db.Tournaments.AddRange(inA, inB);

        await db.SaveChangesAsync();

        _clubA = a.Id;
        _clubB = b.Id;
        _tournamentInA = inA.Id;
        _tournamentInB = inB.Id;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    /// <summary>Gibt dem Platz eine Öffnungszeit und eine Sperre mit interner Notiz.</summary>
    private static void Equip(Court court, string note)
    {
        court.AddAvailability(
            Guid.NewGuid(), DayOfWeek.Saturday,
            new TimeOnly(8, 0), new TimeOnly(22, 0),
            new DateOnly(2026, 1, 1));

        court.AddBlock(
            Guid.NewGuid(),
            new TimeSlot(
                new DateTimeOffset(2026, 5, 16, 14, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 16, 18, 0, 0, TimeSpan.Zero)),
            BlockReason.LeagueMatch,
            note);
    }

    private static Tournament NewTournament(Guid clubId, Guid templateId, string name) =>
        new(Guid.NewGuid(), clubId, name, new DateOnly(2026, 5, 16), new DateOnly(2026, 5, 17), templateId);

    private static UserPrincipal DirectorOf(Guid tournamentId)
    {
        var userId = Guid.NewGuid();

        return new UserPrincipal(
            userId,
            [new RoleAssignment(
                Guid.NewGuid(), userId, Role.TournamentDirector, ResourceScope.Tournament(tournamentId))]);
    }

    [Fact]
    public async Task Ein_Turnierleiter_sieht_nur_den_Verein_seines_Turniers()
    {
        _database.ActingAs = DirectorOf(_tournamentInA);
        await using var db = _database.NewContext();

        Assert.Equal([_clubA], (await db.Clubs.ToListAsync()).Select(c => c.Id));
    }

    [Fact]
    public async Task Der_gezielte_Zugriff_auf_einen_fremden_Verein_liefert_nichts()
    {
        // Entscheidend: nicht „Zugriff verweigert", sondern „existiert nicht".
        // Ein 403 würde die Existenz des Vereins verraten.
        _database.ActingAs = DirectorOf(_tournamentInA);
        await using var db = _database.NewContext();

        Assert.Null(await db.Clubs.FirstOrDefaultAsync(c => c.Id == _clubB));
    }

    [Fact]
    public async Task Auch_die_Plaetze_eines_fremden_Vereins_bleiben_verborgen()
    {
        // Der Filter muss an jeder abhängigen Entität hängen. Nur am Verein wäre
        // er wirkungslos, sobald jemand direkt über die Plätze abfragt.
        _database.ActingAs = DirectorOf(_tournamentInA);
        await using var db = _database.NewContext();

        var courts = await db.Courts.ToListAsync();

        Assert.All(courts, court => Assert.Equal(_clubA, court.ClubId));
        Assert.Single(courts);
    }

    [Fact]
    public async Task Auch_Sperren_und_Oeffnungszeiten_fremder_Vereine_bleiben_verborgen()
    {
        // Regression: der Filter hing nur an Verein und Platz. Über die
        // Navigation vom Verein aus war das folgenlos, eine direkte Abfrage der
        // Kindtabelle lieferte aber die internen Notizen fremder Vereine.
        _database.ActingAs = DirectorOf(_tournamentInA);
        await using var db = _database.NewContext();

        var notes = await db.Set<CourtBlock>().Select(b => b.Note).ToListAsync();
        var windows = await db.Set<AvailabilityWindow>().CountAsync();

        Assert.Equal(["Nur für Verein A"], notes);
        Assert.Equal(1, windows);
    }

    [Fact]
    public async Task Ein_SystemAdmin_sieht_alle_Vereine()
    {
        var admin = Guid.NewGuid();
        _database.ActingAs = new UserPrincipal(
            admin,
            [new RoleAssignment(Guid.NewGuid(), admin, Role.SystemAdmin, ResourceScope.Global)]);

        await using var db = _database.NewContext();

        Assert.Equal(2, await db.Clubs.CountAsync());
    }

    [Fact]
    public async Task Ein_angemeldeter_Benutzer_ohne_Turnier_sieht_keinen_Verein()
    {
        // Die neue Grenze, ausdrücklich geprüft: früher genügte irgendeine
        // Vereinsrolle, um einen Verein zu sehen. Jetzt führt der einzige Weg
        // dorthin über ein Turnier.
        var stranger = Guid.NewGuid();
        _database.ActingAs = new UserPrincipal(stranger, []);

        await using var db = _database.NewContext();

        Assert.Empty(await db.Clubs.ToListAsync());
        Assert.Empty(await db.Courts.ToListAsync());
    }

    [Fact]
    public async Task Ein_nicht_angemeldeter_Aufrufer_sieht_keinen_Verein()
    {
        _database.ActingAs = UserPrincipal.Anonymous;
        await using var db = _database.NewContext();

        Assert.Empty(await db.Clubs.ToListAsync());
    }

    [Fact]
    public async Task Der_Systemkontext_sieht_alles()
    {
        _database.ActingAs = UserPrincipal.System;
        await using var db = _database.NewContext();

        Assert.Equal(2, await db.Clubs.CountAsync());
    }

    [Fact]
    public async Task Der_Filter_wird_je_Benutzer_neu_ausgewertet()
    {
        // EF Core legt kompilierte Abfragepläne im Cache ab, und das Filter-Lambda
        // schließt über den DbContext ab, der das Modell gebaut hat. Würde der
        // Filter die Turnier-Id als Konstante statt als Parameter einbauen, bekäme
        // der zweite Benutzer die Ergebnisse des ersten — ein Datenleck, das erst
        // unter Last auffiele. Jeder Kontext hat hier seine eigene
        // IUserContext-Instanz, genau wie jeder Request in der Anwendung.
        _database.ActingAs = DirectorOf(_tournamentInA);
        await using (var first = _database.NewContext())
        {
            Assert.Equal([_clubA], (await first.Clubs.ToListAsync()).Select(c => c.Id));
        }

        _database.ActingAs = DirectorOf(_tournamentInB);
        await using (var second = _database.NewContext())
        {
            Assert.Equal([_clubB], (await second.Clubs.ToListAsync()).Select(c => c.Id));
        }
    }
}
