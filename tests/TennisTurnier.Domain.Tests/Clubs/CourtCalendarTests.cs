using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Tests.Clubs;

public sealed class CourtCalendarTests
{
    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    private readonly Club _club = new(Guid.NewGuid(), "TC Musterstadt", "Europe/Vienna");
    private readonly CourtCalendar _calendar = new(Vienna);

    private Court NewCourt() =>
        _club.AddCourt(Guid.NewGuid(), $"Platz {_club.Courts.Count + 1}", CourtSurface.Clay, CourtLocation.Outdoor);

    /// <summary>Lokale Vereinszeit als absoluter Zeitpunkt.</summary>
    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute = 0)
    {
        var unspecified = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, Vienna.GetUtcOffset(unspecified));
    }

    private static TimeSlot Day(int year, int month, int day) =>
        new(Local(year, month, day, 0), Local(year, month, day, 0).AddDays(1));

    [Fact]
    public void Ohne_Oeffnungszeiten_ist_nichts_frei()
    {
        var court = NewCourt();

        Assert.Empty(_calendar.FreeWindows(court, Day(2026, 5, 16)));
    }

    [Fact]
    public void Eine_woechentliche_Regel_gilt_nur_an_ihrem_Wochentag()
    {
        var court = NewCourt();
        court.AddAvailability(
            Guid.NewGuid(), DayOfWeek.Saturday,
            new TimeOnly(8, 0), new TimeOnly(22, 0),
            new DateOnly(2026, 1, 1));

        var saturday = _calendar.FreeWindows(court, Day(2026, 5, 16));
        var sunday = _calendar.FreeWindows(court, Day(2026, 5, 17));

        Assert.Equal([new TimeSlot(Local(2026, 5, 16, 8), Local(2026, 5, 16, 22))], saturday);
        Assert.Empty(sunday);
    }

    [Fact]
    public void Eine_Regel_gilt_nicht_vor_ihrem_Gueltigkeitsbeginn()
    {
        var court = NewCourt();
        court.AddAvailability(
            Guid.NewGuid(), DayOfWeek.Saturday,
            new TimeOnly(8, 0), new TimeOnly(22, 0),
            validFrom: new DateOnly(2026, 6, 1));

        Assert.Empty(_calendar.FreeWindows(court, Day(2026, 5, 16)));
        Assert.NotEmpty(_calendar.FreeWindows(court, Day(2026, 6, 6)));
    }

    [Fact]
    public void Eine_Regel_gilt_nicht_nach_ihrem_Gueltigkeitsende()
    {
        var court = NewCourt();
        court.AddAvailability(
            Guid.NewGuid(), DayOfWeek.Saturday,
            new TimeOnly(8, 0), new TimeOnly(22, 0),
            validFrom: new DateOnly(2026, 1, 1),
            validUntil: new DateOnly(2026, 5, 31));

        Assert.NotEmpty(_calendar.FreeWindows(court, Day(2026, 5, 16)));
        Assert.Empty(_calendar.FreeWindows(court, Day(2026, 6, 6)));
    }

    [Fact]
    public void Eine_Sperre_in_der_Mitte_zerteilt_den_Tag()
    {
        var court = NewCourt();
        court.AddAvailability(
            Guid.NewGuid(), DayOfWeek.Saturday,
            new TimeOnly(8, 0), new TimeOnly(22, 0),
            new DateOnly(2026, 1, 1));
        court.AddBlock(
            Guid.NewGuid(),
            new TimeSlot(Local(2026, 5, 16, 14), Local(2026, 5, 16, 18)),
            BlockReason.LeagueMatch,
            "Herren 45 gegen TC Nachbarort");

        var free = _calendar.FreeWindows(court, Day(2026, 5, 16));

        Assert.Equal(
            [
                new TimeSlot(Local(2026, 5, 16, 8), Local(2026, 5, 16, 14)),
                new TimeSlot(Local(2026, 5, 16, 18), Local(2026, 5, 16, 22)),
            ],
            free);
    }

    [Fact]
    public void Mehrere_Sperren_werden_alle_beruecksichtigt()
    {
        var court = NewCourt();
        court.AddAvailability(
            Guid.NewGuid(), DayOfWeek.Saturday,
            new TimeOnly(8, 0), new TimeOnly(22, 0),
            new DateOnly(2026, 1, 1));
        court.AddBlock(Guid.NewGuid(), new TimeSlot(Local(2026, 5, 16, 10), Local(2026, 5, 16, 12)), BlockReason.Training);
        court.AddBlock(Guid.NewGuid(), new TimeSlot(Local(2026, 5, 16, 18), Local(2026, 5, 16, 20)), BlockReason.Maintenance);

        var free = _calendar.FreeWindows(court, Day(2026, 5, 16));

        Assert.Equal(
            [
                new TimeSlot(Local(2026, 5, 16, 8), Local(2026, 5, 16, 10)),
                new TimeSlot(Local(2026, 5, 16, 12), Local(2026, 5, 16, 18)),
                new TimeSlot(Local(2026, 5, 16, 20), Local(2026, 5, 16, 22)),
            ],
            free);
    }

    [Fact]
    public void Der_abgefragte_Bereich_schneidet_die_Oeffnungszeit_zu()
    {
        var court = NewCourt();
        court.AddAvailability(
            Guid.NewGuid(), DayOfWeek.Saturday,
            new TimeOnly(8, 0), new TimeOnly(22, 0),
            new DateOnly(2026, 1, 1));

        var free = _calendar.FreeWindows(
            court,
            new TimeSlot(Local(2026, 5, 16, 12), Local(2026, 5, 16, 16)));

        Assert.Equal([new TimeSlot(Local(2026, 5, 16, 12), Local(2026, 5, 16, 16))], free);
    }

    [Fact]
    public void Ein_Bereich_ueber_mehrere_Tage_liefert_ein_Fenster_je_Tag()
    {
        var court = NewCourt();
        foreach (var day in new[] { DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            court.AddAvailability(
                Guid.NewGuid(), day,
                new TimeOnly(9, 0), new TimeOnly(20, 0),
                new DateOnly(2026, 1, 1));
        }

        var free = _calendar.FreeWindows(
            court,
            new TimeSlot(Local(2026, 5, 16, 0), Local(2026, 5, 18, 0)));

        Assert.Equal(
            [
                new TimeSlot(Local(2026, 5, 16, 9), Local(2026, 5, 16, 20)),
                new TimeSlot(Local(2026, 5, 17, 9), Local(2026, 5, 17, 20)),
            ],
            free);
    }

    [Fact]
    public void Ein_stillgelegter_Platz_ist_nie_frei()
    {
        var court = NewCourt();
        court.AddAvailability(
            Guid.NewGuid(), DayOfWeek.Saturday,
            new TimeOnly(8, 0), new TimeOnly(22, 0),
            new DateOnly(2026, 1, 1));
        court.Deactivate();

        Assert.Empty(_calendar.FreeWindows(court, Day(2026, 5, 16)));
    }

    [Fact]
    public void Zwei_Regeln_am_selben_Tag_werden_zusammengefasst()
    {
        // Vormittags- und Nachmittagsbetrieb, die nahtlos aneinander grenzen:
        // das Ergebnis muss ein durchgehendes Fenster sein, keine zwei.
        var court = NewCourt();
        court.AddAvailability(Guid.NewGuid(), DayOfWeek.Saturday, new TimeOnly(8, 0), new TimeOnly(13, 0), new DateOnly(2026, 1, 1));
        court.AddAvailability(Guid.NewGuid(), DayOfWeek.Saturday, new TimeOnly(13, 0), new TimeOnly(22, 0), new DateOnly(2026, 1, 1));

        var free = _calendar.FreeWindows(court, Day(2026, 5, 16));

        Assert.Equal([new TimeSlot(Local(2026, 5, 16, 8), Local(2026, 5, 16, 22))], free);
    }

    [Fact]
    public void Der_Tag_der_Zeitumstellung_im_Fruehjahr_ist_eine_Stunde_kuerzer()
    {
        // 29.03.2026: In Europa springt die Uhr um 02:00 auf 03:00. Ein Fenster
        // von 00:00 bis 06:00 lokaler Zeit dauert daher nur fünf Stunden.
        var court = NewCourt();
        court.AddAvailability(
            Guid.NewGuid(), DayOfWeek.Sunday,
            new TimeOnly(0, 0), new TimeOnly(6, 0),
            new DateOnly(2026, 1, 1));

        var free = _calendar.FreeWindows(
            court,
            new TimeSlot(
                new DateTimeOffset(2026, 3, 28, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.Zero)));

        var window = Assert.Single(free);
        Assert.Equal(TimeSpan.FromHours(5), window.Duration);
    }

    [Fact]
    public void Der_Tag_der_Zeitumstellung_im_Herbst_ist_eine_Stunde_laenger()
    {
        // 25.10.2026: Die Uhr fällt um 03:00 auf 02:00 zurück, die Stunde von
        // 02:00 bis 03:00 existiert doppelt. Öffnungszeit auf die frühere,
        // Schlusszeit auf die spätere Ausprägung — das Fenster deckt beide ab.
        var court = NewCourt();
        court.AddAvailability(
            Guid.NewGuid(), DayOfWeek.Sunday,
            new TimeOnly(0, 0), new TimeOnly(6, 0),
            new DateOnly(2026, 1, 1));

        var free = _calendar.FreeWindows(
            court,
            new TimeSlot(
                new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero)));

        var window = Assert.Single(free);
        Assert.Equal(TimeSpan.FromHours(7), window.Duration);
    }

    [Fact]
    public void Ein_normaler_Tag_hat_genau_die_angegebene_Dauer()
    {
        // Gegenprobe zu den beiden Umstellungstests: ohne Zeitumstellung darf
        // die Auflösung nichts verschieben.
        var court = NewCourt();
        court.AddAvailability(
            Guid.NewGuid(), DayOfWeek.Saturday,
            new TimeOnly(8, 0), new TimeOnly(22, 0),
            new DateOnly(2026, 1, 1));

        var window = Assert.Single(_calendar.FreeWindows(court, Day(2026, 5, 16)));

        Assert.Equal(TimeSpan.FromHours(14), window.Duration);
    }
}
