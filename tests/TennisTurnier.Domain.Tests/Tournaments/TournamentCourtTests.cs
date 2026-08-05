using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Tournaments;

/// <summary>
/// Der Platz eines Turniers und die Zeiten, zu denen er ihm gehört.
///
/// Reserviert wird außerhalb der Anwendung. Was hier steht, ist deshalb kein
/// Wochentagsraster mit Sperren, sondern die Liste der Stunden, die tatsächlich
/// zugesagt sind — es gibt nichts abzuziehen, nur zusammenzuführen.
/// </summary>
public sealed class TournamentCourtTests
{
    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    private readonly Guid _tournamentId = Guid.NewGuid();

    private TournamentCourt NewCourt() =>
        new(Guid.NewGuid(), _tournamentId, "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

    /// <summary>Lokale Zeit als absoluter Zeitpunkt.</summary>
    private static DateTimeOffset Local(int year, int month, int day, int hour)
    {
        var unspecified = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, Vienna.GetUtcOffset(unspecified));
    }

    private static TimeSlot Slot(int day, int fromHour, int toHour) =>
        new(Local(2026, 5, day, fromHour), Local(2026, 5, day, toHour));

    private static TimeSlot Days(int from, int to) =>
        new(Local(2026, 5, from, 0), Local(2026, 5, to, 0));

    [Fact]
    public void Ohne_Platzzeiten_ist_nichts_frei()
    {
        Assert.Empty(NewCourt().FreeWindows(Days(16, 18)));
    }

    [Fact]
    public void Eine_Platzzeit_gilt_an_ihrem_Tag_und_an_keinem_anderen()
    {
        var court = NewCourt();
        court.AddWindow(Guid.NewGuid(), Slot(16, 9, 18));

        Assert.Equal([Slot(16, 9, 18)], court.FreeWindows(Days(16, 17)));
        Assert.Empty(court.FreeWindows(Days(17, 18)));
    }

    [Fact]
    public void Zwei_Platzzeiten_desselben_Tages_werden_zusammengefasst()
    {
        // Vormittag und Nachmittag, nahtlos aneinander: das Ergebnis muss ein
        // durchgehendes Fenster sein. Zwei Fenster hießen, dass ein Match über
        // die Grenze hinweg nicht mehr hineinpasste.
        var court = NewCourt();
        court.AddWindow(Guid.NewGuid(), Slot(16, 9, 13));
        court.AddWindow(Guid.NewGuid(), Slot(16, 13, 18));

        Assert.Equal([Slot(16, 9, 18)], court.FreeWindows(Days(16, 17)));
    }

    [Fact]
    public void Eine_Luecke_zwischen_zwei_Platzzeiten_bleibt_eine_Luecke()
    {
        // Die Gegenprobe: was nicht reserviert ist, wird nicht überbrückt.
        var court = NewCourt();
        court.AddWindow(Guid.NewGuid(), Slot(16, 9, 12));
        court.AddWindow(Guid.NewGuid(), Slot(16, 15, 18));

        Assert.Equal([Slot(16, 9, 12), Slot(16, 15, 18)], court.FreeWindows(Days(16, 17)));
    }

    [Fact]
    public void Der_abgefragte_Bereich_schneidet_die_Platzzeit_zu()
    {
        var court = NewCourt();
        court.AddWindow(Guid.NewGuid(), Slot(16, 9, 18));

        Assert.Equal([Slot(16, 12, 16)], court.FreeWindows(Slot(16, 12, 16)));
    }

    [Fact]
    public void Ueberlappende_Platzzeiten_auf_demselben_Platz_werden_abgewiesen()
    {
        // Dieselbe Stunde zweimal zu zählen hieße, den Platz für doppelt
        // vorhanden zu halten.
        var court = NewCourt();
        court.AddWindow(Guid.NewGuid(), Slot(16, 9, 13));

        var ex = Assert.Throws<DomainException>(
            () => court.AddWindow(Guid.NewGuid(), Slot(16, 12, 18)));

        Assert.Contains("überschneidet", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Aneinander_grenzende_Platzzeiten_sind_keine_Ueberlappung()
    {
        var court = NewCourt();
        court.AddWindow(Guid.NewGuid(), Slot(16, 9, 13));

        court.AddWindow(Guid.NewGuid(), Slot(16, 13, 18));

        Assert.Equal(2, court.Windows.Count);
    }

    [Fact]
    public void Platzzeiten_zweier_Plaetze_stoeren_einander_nicht()
    {
        var first = NewCourt();
        var second = NewCourt();

        first.AddWindow(Guid.NewGuid(), Slot(16, 9, 18));
        second.AddWindow(Guid.NewGuid(), Slot(16, 9, 18));

        Assert.Single(first.Windows);
        Assert.Single(second.Windows);
    }

    [Fact]
    public void Eine_rueckwaerts_laufende_Platzzeit_wird_abgewiesen()
    {
        Assert.Throws<DomainException>(
            () => new TimeSlot(Local(2026, 5, 16, 18), Local(2026, 5, 16, 9)));
    }

    [Fact]
    public void Eine_entfernte_Platzzeit_gibt_ihre_Stunden_wieder_frei()
    {
        var court = NewCourt();
        var window = court.AddWindow(Guid.NewGuid(), Slot(16, 9, 18));

        court.RemoveWindow(window.Id);

        Assert.Empty(court.Windows);
        Assert.Empty(court.FreeWindows(Days(16, 17)));
    }

    [Fact]
    public void Eine_nicht_vorhandene_Platzzeit_laesst_sich_nicht_entfernen()
    {
        Assert.Throws<DomainException>(() => NewCourt().RemoveWindow(Guid.NewGuid()));
    }

    [Fact]
    public void Ein_stillgelegter_Platz_ist_nie_frei()
    {
        var court = NewCourt();
        court.AddWindow(Guid.NewGuid(), Slot(16, 9, 18));
        court.Deactivate();

        Assert.Empty(court.FreeWindows(Days(16, 17)));

        court.Reactivate();
        Assert.NotEmpty(court.FreeWindows(Days(16, 17)));
    }

    [Fact]
    public void Ein_Platz_ohne_Namen_wird_abgewiesen()
    {
        Assert.Throws<DomainException>(
            () => new TournamentCourt(
                Guid.NewGuid(), _tournamentId, "  ", CourtSurface.Clay, CourtLocation.Outdoor));
    }

    [Fact]
    public void Ein_Platz_ohne_Turnier_wird_abgewiesen()
    {
        Assert.Throws<DomainException>(
            () => new TournamentCourt(
                Guid.NewGuid(), Guid.Empty, "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor));
    }

    [Fact]
    public void Eine_Platzzeit_kennt_ihr_Turnier_ohne_Umweg_ueber_den_Platz()
    {
        // Der Query-Filter aus ADR-0004 arbeitet auf der Menge der sichtbaren
        // Turniere. Ohne diese Kennung wäre er zweistufig.
        var court = NewCourt();

        var window = court.AddWindow(Guid.NewGuid(), Slot(16, 9, 18));

        Assert.Equal(_tournamentId, window.TournamentId);
        Assert.Equal(court.Id, window.CourtId);
    }
}
