using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Tournaments;

public sealed class VenueTests
{
    [Fact]
    public void Ein_Ort_braucht_einen_Namen()
    {
        Assert.Throws<DomainException>(() => new Venue("  ", null, null, "Europe/Vienna"));
    }

    [Fact]
    public void Ein_Ort_braucht_eine_bekannte_Zeitzone()
    {
        // Lieber hier scheitern als beim Aufbau des Spielplans: dort wäre die
        // Ursache eine Zeichenkette, die vor Wochen eingegeben wurde.
        var ex = Assert.Throws<DomainException>(
            () => new Venue("TC Maria Alm", null, null, "Europe/Atlantis"));

        Assert.Contains("Europe/Atlantis", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_leere_Zeitzone_wird_abgewiesen()
    {
        Assert.Throws<DomainException>(() => new Venue("TC Maria Alm", null, null, "  "));
    }

    [Fact]
    public void Adresse_und_Stadt_sind_freiwillig()
    {
        var venue = new Venue("TC Maria Alm", null, null, "Europe/Vienna");

        Assert.Null(venue.Address);
        Assert.Null(venue.City);
        Assert.Equal("TC Maria Alm", venue.ToString());
    }

    [Fact]
    public void Leerraum_gilt_als_keine_Angabe()
    {
        var venue = new Venue(" TC Maria Alm ", "   ", " Maria Alm ", "Europe/Vienna");

        Assert.Equal("TC Maria Alm", venue.Name);
        Assert.Null(venue.Address);
        Assert.Equal("Maria Alm", venue.City);
        Assert.Equal("TC Maria Alm, Maria Alm", venue.ToString());
    }

    [Fact]
    public void Die_Zeitzone_laesst_sich_aufloesen()
    {
        var venue = new Venue("TC Maria Alm", null, null, "Europe/Vienna");

        var mai = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(TimeSpan.FromHours(2), venue.TimeZone.GetUtcOffset(mai));
    }

    [Fact]
    public void Zwei_gleiche_Orte_sind_gleich()
    {
        // Ein Wertobjekt: es hat keine Kennung, nur einen Inhalt.
        Assert.Equal(
            new Venue("TC Maria Alm", "Am Gries 1", "Maria Alm", "Europe/Vienna"),
            new Venue("TC Maria Alm", "Am Gries 1", "Maria Alm", "Europe/Vienna"));
    }
}
