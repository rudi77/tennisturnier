using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Tests.Clubs;

public sealed class CourtAvailabilityTests
{
    private readonly Court _court = new Club(Guid.NewGuid(), "TC Musterstadt", "Europe/Vienna")
        .AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

    private static readonly DateOnly Season = new(2026, 1, 1);

    private AvailabilityWindow Add(
        DayOfWeek day,
        int fromHour,
        int toHour,
        DateOnly? validFrom = null,
        DateOnly? validUntil = null) =>
        _court.AddAvailability(
            Guid.NewGuid(), day,
            new TimeOnly(fromHour, 0), new TimeOnly(toHour, 0),
            validFrom ?? Season, validUntil);

    [Fact]
    public void Eine_rueckwaerts_laufende_Oeffnungszeit_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => Add(DayOfWeek.Monday, 22, 8));
    }

    [Fact]
    public void Eine_Oeffnungszeit_ohne_Dauer_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => Add(DayOfWeek.Monday, 8, 8));
    }

    [Fact]
    public void Ein_rueckwaerts_laufender_Gueltigkeitszeitraum_ist_ungueltig()
    {
        Assert.Throws<DomainException>(
            () => Add(DayOfWeek.Monday, 8, 22, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public void Ueberlappende_Regeln_am_selben_Wochentag_werden_abgewiesen()
    {
        Add(DayOfWeek.Monday, 8, 14);

        Assert.Throws<DomainException>(() => Add(DayOfWeek.Monday, 12, 20));
    }

    [Fact]
    public void Aneinander_grenzende_Regeln_sind_erlaubt()
    {
        Add(DayOfWeek.Monday, 8, 14);
        Add(DayOfWeek.Monday, 14, 22);

        Assert.Equal(2, _court.Availability.Count);
    }

    [Fact]
    public void Dieselbe_Uhrzeit_an_verschiedenen_Wochentagen_ist_kein_Konflikt()
    {
        Add(DayOfWeek.Monday, 8, 22);
        Add(DayOfWeek.Tuesday, 8, 22);

        Assert.Equal(2, _court.Availability.Count);
    }

    [Fact]
    public void Getrennte_Sommer_und_Winterregeln_sind_kein_Konflikt()
    {
        // Der eigentliche Grund für den Gültigkeitszeitraum: derselbe Wochentag,
        // dieselbe Uhrzeit, aber Saisons, die sich nicht überschneiden.
        Add(DayOfWeek.Monday, 8, 22, new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 30));
        Add(DayOfWeek.Monday, 8, 22, new DateOnly(2026, 10, 1), new DateOnly(2027, 3, 31));

        Assert.Equal(2, _court.Availability.Count);
    }

    [Fact]
    public void Ueberlappende_Saisons_mit_gleicher_Uhrzeit_sind_ein_Konflikt()
    {
        Add(DayOfWeek.Monday, 8, 22, new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 30));

        Assert.Throws<DomainException>(
            () => Add(DayOfWeek.Monday, 8, 22, new DateOnly(2026, 9, 1), new DateOnly(2027, 3, 31)));
    }

    [Fact]
    public void Eine_offene_Regel_kollidiert_mit_jeder_spaeteren_Saison()
    {
        Add(DayOfWeek.Monday, 8, 22, new DateOnly(2026, 4, 1), validUntil: null);

        Assert.Throws<DomainException>(
            () => Add(DayOfWeek.Monday, 10, 12, new DateOnly(2030, 1, 1)));
    }

    [Theory]
    [InlineData("2026-05-18", true)]   // Montag im Gültigkeitszeitraum
    [InlineData("2026-05-19", false)]  // Dienstag
    [InlineData("2026-03-30", false)]  // Montag vor Saisonbeginn
    [InlineData("2026-10-05", false)]  // Montag nach Saisonende
    public void AppliesOn_beruecksichtigt_Wochentag_und_Saison(string date, bool expected)
    {
        var window = Add(DayOfWeek.Monday, 8, 22, new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 30));

        Assert.Equal(expected, window.AppliesOn(DateOnly.Parse(date)));
    }

    [Fact]
    public void Eine_Oeffnungszeit_laesst_sich_wieder_entfernen()
    {
        var window = Add(DayOfWeek.Monday, 8, 22);

        _court.RemoveAvailability(window.Id);

        Assert.Empty(_court.Availability);
    }

    [Fact]
    public void Das_Entfernen_einer_unbekannten_Oeffnungszeit_schlaegt_fehl()
    {
        Assert.Throws<DomainException>(() => _court.RemoveAvailability(Guid.NewGuid()));
    }

    [Fact]
    public void Sperren_lassen_sich_anlegen_und_entfernen()
    {
        var period = new TimeSlot(
            new DateTimeOffset(2026, 5, 16, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 16, 18, 0, 0, TimeSpan.Zero));

        var block = _court.AddBlock(Guid.NewGuid(), period, BlockReason.Weather, "  Dauerregen  ");

        Assert.Equal("Dauerregen", block.Note);
        Assert.Equal([block], _court.Blocks);

        _court.RemoveBlock(block.Id);
        Assert.Empty(_court.Blocks);
    }

    [Fact]
    public void Das_Entfernen_einer_unbekannten_Sperre_schlaegt_fehl()
    {
        Assert.Throws<DomainException>(() => _court.RemoveBlock(Guid.NewGuid()));
    }
}
