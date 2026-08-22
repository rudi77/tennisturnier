using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Common;

/// <summary>
/// Die kleinen Bausteine, auf denen alles andere steht: Identität, Zeitfenster,
/// Teilnehmer, Tabellen.
///
/// Sie sind unspektakulär und werden deshalb gern übersprungen. Genau dort
/// entstehen aber die Fehler, die sich nirgends festmachen lassen — zwei
/// Turniere, die sich für dasselbe halten, ein Fenster ohne Platz, eine Meldung
/// ohne Turnier.
/// </summary>
public sealed class BausteineTests
{
    private static readonly DateTimeOffset Morgens =
        new(2026, 5, 16, 9, 0, 0, TimeSpan.FromHours(2));

    private sealed class Turnierartig(Guid id) : Entity(id);

    private sealed class Andersartig(Guid id) : Entity(id);

    [Fact]
    public void Eine_Entitaet_ohne_Id_gibt_es_nicht()
    {
        var fehler = Assert.Throws<DomainException>(() => new Turnierartig(Guid.Empty));

        Assert.Contains("braucht eine Id", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gleich_ist_nur_dasselbe_bei_gleicher_Art()
    {
        var id = Guid.NewGuid();
        var eine = new Turnierartig(id);

        // Dieselbe Id, dieselbe Art: dasselbe Ding, auch als zwei Objekte.
        Assert.Equal(eine, new Turnierartig(id));
        Assert.Equal(eine.GetHashCode(), new Turnierartig(id).GetHashCode());

        // Dieselbe Id, andere Art: zwei Dinge. Ohne diese Bedingung hielte ein
        // Wörterbuch eine Meldung für ihren Teilnehmer.
        Assert.NotEqual<object>(eine, new Andersartig(id));
        Assert.NotEqual(eine, new Turnierartig(Guid.NewGuid()));
        Assert.False(eine.Equals("kein Ding"));
    }

    [Fact]
    public void Eine_Zeit_braucht_eine_Zone()
    {
        Assert.Throws<ArgumentNullException>(() => new LocalTime(null!));
    }

    [Fact]
    public void Eine_absurde_Zeitzone_wird_gemeldet_statt_gesucht()
    {
        // Eine Umstellung um fünf Stunden gibt es nirgends. Sie steht hier für
        // eine kaputte Zonendefinition: die Suche nach dem ersten gültigen
        // Zeitpunkt bricht nach vier Stunden ab, statt endlos zu laufen.
        var regel = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(5),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1));

        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "Absurdien", TimeSpan.Zero, "Absurdien", "Absurdien", "Absurdien (Sommer)", [regel]);

        var zeit = new LocalTime(zone);

        var fehler = Assert.Throws<DomainException>(() =>
            zeit.Resolve(new DateOnly(2026, 3, 1), new TimeOnly(2, 30), LocalTime.Ambiguity.Earliest));

        Assert.Contains("Absurdien", fehler.Message, StringComparison.Ordinal);
        Assert.Contains("kein gültiger Zeitpunkt", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_Zeitfenster_endet_bevor_es_endet()
    {
        var fenster = new TimeSlot(Morgens, Morgens.AddHours(2));

        Assert.True(fenster.Contains(Morgens));
        Assert.True(fenster.Contains(Morgens.AddHours(1)));

        // Halboffen: das Ende gehört nicht mehr dazu, der Beginn davor auch nicht.
        Assert.False(fenster.Contains(Morgens.AddHours(2)));
        Assert.False(fenster.Contains(Morgens.AddMinutes(-1)));
    }

    [Fact]
    public void Ein_umschlossenes_Fenster_verlaengert_das_lange_nicht()
    {
        // Das zweite Fenster liegt vollständig im ersten. Wer hier blind das
        // spätere Ende nimmt, verkürzt den Öffnungszeitraum.
        var lang = new TimeSlot(Morgens, Morgens.AddHours(8));
        var kurz = new TimeSlot(Morgens.AddHours(2), Morgens.AddHours(3));

        var verschmolzen = Assert.Single(TimeSlot.Merge([lang, kurz]));

        Assert.Equal(lang.Start, verschmolzen.Start);
        Assert.Equal(lang.End, verschmolzen.End);
    }

    [Fact]
    public void Ein_Platzzeitfenster_braucht_Turnier_und_Platz()
    {
        var zeitraum = new TimeSlot(Morgens, Morgens.AddHours(4));

        Assert.Throws<DomainException>(() =>
            new CourtWindow(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), zeitraum));

        Assert.Throws<DomainException>(() =>
            new CourtWindow(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, zeitraum));
    }

    [Fact]
    public void Zwei_Fenster_auf_verschiedenen_Plaetzen_stoeren_einander_nicht()
    {
        var turnier = Guid.NewGuid();
        var zeitraum = new TimeSlot(Morgens, Morgens.AddHours(4));

        var platz1 = new CourtWindow(Guid.NewGuid(), turnier, Guid.NewGuid(), zeitraum);
        var platz2 = new CourtWindow(Guid.NewGuid(), turnier, Guid.NewGuid(), zeitraum);
        var derselbePlatz = new CourtWindow(Guid.NewGuid(), turnier, platz1.CourtId, zeitraum);

        Assert.False(platz1.ConflictsWith(platz2));
        Assert.True(platz1.ConflictsWith(derselbePlatz));
        Assert.Equal(zeitraum.ToString(), platz1.ToString());
    }

    [Fact]
    public void Ein_Doppel_braucht_zwei_Spieler()
    {
        var einer = Guid.NewGuid();

        Assert.Throws<DomainException>(() =>
            Participant.Team(Guid.NewGuid(), Guid.Empty, einer, "Wer und Niemand"));

        Assert.Throws<DomainException>(() =>
            Participant.Team(Guid.NewGuid(), einer, Guid.Empty, "Wer und Niemand"));
    }

    [Fact]
    public void Eine_Meldung_braucht_Turnier_und_Teilnehmer()
    {
        Assert.Throws<DomainException>(() =>
            new TournamentEntry(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), null));

        Assert.Throws<DomainException>(() =>
            new TournamentEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, null));
    }

    [Fact]
    public void Ein_Spieler_heisst_Nachname_Vorname()
    {
        var spieler = new Player(Guid.NewGuid(), " Anna ", " Müller ");

        Assert.Equal("Müller, Anna", spieler.DisplayName);
        Assert.Equal(PlayerContact.Empty, spieler.Contact);
    }

    [Fact]
    public void Ein_leerer_Vorschlag_wirft_nichts_um()
    {
        var leer = ScheduleProposal.Empty;

        Assert.Empty(leer.Assignments);
        Assert.Empty(leer.Unscheduled);
        Assert.Empty(leer.Violations);
        Assert.True(leer.Diff.IsEmpty);
        Assert.Equal(0, leer.Diff.Total);

        var bewegt = new ScheduleDiff(Unchanged: 3, Added: 1, Moved: 2, Removed: 0);

        Assert.False(bewegt.IsEmpty);
        Assert.Equal(6, bewegt.Total);
    }

    [Fact]
    public void Ein_Match_ohne_eigene_Schaetzung_dauert_die_Vorgabe()
    {
        var problem = new SchedulingProblem(
            Matches: [],
            PlayersByEntry: new Dictionary<Guid, IReadOnlyList<Guid>>(),
            Courts: [],
            DurationByMatch: new Dictionary<Guid, TimeSpan>(),
            MinimumRest: TimeSpan.FromMinutes(30),
            Existing: []);

        Assert.Equal(MatchDuration.Default, problem.DurationOf(Guid.NewGuid()));
    }

    [Fact]
    public void Eine_leere_Tabelle_kennt_keine_Gruppen()
    {
        Assert.Empty(Standings.Empty.Places);
        Assert.Empty(Standings.Empty.Groups);
        Assert.Empty(Standings.Empty.InGroup(null));
    }

    [Fact]
    public void Eine_beendete_Zuweisung_steht_ganz_hinten()
    {
        // Die Reihenfolge der Platzübersicht: was läuft, steht oben; was vorbei
        // ist, verschwindet nach unten.
        Assert.Equal(0, CourtQueue.Liveness(Zuweisung(AssignmentStatus.Running)));
        Assert.Equal(1, CourtQueue.Liveness(Zuweisung(AssignmentStatus.Called)));
        Assert.Equal(2, CourtQueue.Liveness(Zuweisung(AssignmentStatus.Planned)));
        Assert.Equal(3, CourtQueue.Liveness(Zuweisung(AssignmentStatus.Suspended)));
        Assert.Equal(4, CourtQueue.Liveness(Zuweisung(AssignmentStatus.Finished)));
    }

    private static CourtAssignment Zuweisung(AssignmentStatus status)
    {
        var zuweisung = new CourtAssignment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            TimeSpan.FromMinutes(60),
            AssignmentSource.Manual);

        switch (status)
        {
            case AssignmentStatus.Planned:
                break;
            case AssignmentStatus.Called:
                zuweisung.Call();
                break;
            case AssignmentStatus.Running:
                zuweisung.Call();
                zuweisung.Start(Morgens);
                break;
            case AssignmentStatus.Suspended:
                zuweisung.Call();
                zuweisung.Start(Morgens);
                zuweisung.Suspend();
                break;
            default:
                zuweisung.Call();
                zuweisung.Start(Morgens);
                zuweisung.Finish(Morgens.AddHours(1));
                break;
        }

        return zuweisung;
    }
}
