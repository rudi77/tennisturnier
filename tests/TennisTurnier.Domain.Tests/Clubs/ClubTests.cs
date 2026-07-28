using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Tests.Clubs;

public sealed class ClubTests
{
    private static Club NewClub() => new(Guid.NewGuid(), "TC Musterstadt", "Europe/Vienna");

    [Fact]
    public void Ein_Verein_braucht_einen_Namen()
    {
        Assert.Throws<DomainException>(() => new Club(Guid.NewGuid(), "  ", "Europe/Vienna"));
    }

    [Fact]
    public void Ein_Verein_braucht_eine_bekannte_Zeitzone()
    {
        var ex = Assert.Throws<DomainException>(
            () => new Club(Guid.NewGuid(), "TC Musterstadt", "Europe/Atlantis"));

        Assert.Contains("Europe/Atlantis", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Entitaet_braucht_eine_Id()
    {
        Assert.Throws<DomainException>(() => new Club(Guid.Empty, "TC Musterstadt", "Europe/Vienna"));
    }

    [Fact]
    public void Name_und_Ort_werden_getrimmt()
    {
        var club = new Club(Guid.NewGuid(), "  TC Musterstadt  ", "Europe/Vienna", "  Musterstadt  ");

        Assert.Equal("TC Musterstadt", club.Name);
        Assert.Equal("Musterstadt", club.City);
    }

    [Fact]
    public void Ein_leerer_Ort_wird_zu_null()
    {
        Assert.Null(new Club(Guid.NewGuid(), "TC Musterstadt", "Europe/Vienna", "   ").City);
    }

    [Fact]
    public void Ein_Platz_wird_dem_Verein_zugeordnet()
    {
        var club = NewClub();

        var court = club.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

        Assert.Equal(club.Id, court.ClubId);
        Assert.Equal([court], club.Courts);
    }

    [Fact]
    public void Zwei_Plaetze_duerfen_nicht_gleich_heissen()
    {
        var club = NewClub();
        club.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

        // Groß-/Kleinschreibung darf hier nicht unterscheiden: „platz 1" und
        // „Platz 1" wären am Aushang derselbe Platz.
        Assert.Throws<DomainException>(
            () => club.AddCourt(Guid.NewGuid(), "platz 1", CourtSurface.Hard, CourtLocation.Indoor));
    }

    [Fact]
    public void Ein_neuer_Platz_ist_aktiv_und_kein_Center_Court()
    {
        var court = NewClub().AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

        Assert.True(court.IsActive);
        Assert.False(court.IsCenterCourt);
    }

    [Fact]
    public void Ein_Platz_darf_nicht_auf_einen_belegten_Namen_umbenannt_werden()
    {
        // Ohne diese Prüfung bliebe die Eindeutigkeit allein dem eindeutigen
        // Index überlassen, und der Konflikt schlüge als Datenbankfehler durch
        // — für den Aufrufer ein 500 statt einer verständlichen Absage.
        var club = NewClub();
        club.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);
        var second = club.AddCourt(Guid.NewGuid(), "Platz 2", CourtSurface.Clay, CourtLocation.Outdoor);

        Assert.Throws<DomainException>(() => club.RenameCourt(second.Id, "platz 1"));
    }

    [Fact]
    public void Ein_Platz_darf_auf_seinen_eigenen_Namen_umbenannt_werden()
    {
        // Sonst schlüge jede Aktualisierung fehl, die den Namen unverändert lässt.
        var club = NewClub();
        var court = club.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

        club.RenameCourt(court.Id, "Platz 1");

        Assert.Equal("Platz 1", court.Name);
    }

    [Fact]
    public void Ein_Platz_laesst_sich_auf_einen_freien_Namen_umbenennen()
    {
        var club = NewClub();
        var court = club.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

        club.RenameCourt(court.Id, "  Center Court  ");

        Assert.Equal("Center Court", court.Name);
    }

    [Fact]
    public void Das_Umbenennen_eines_unbekannten_Platzes_schlaegt_fehl()
    {
        Assert.Throws<DomainException>(() => NewClub().RenameCourt(Guid.NewGuid(), "Platz 1"));
    }

    [Fact]
    public void Die_Zeitzone_laesst_sich_aufloesen()
    {
        Assert.Equal("Europe/Vienna", NewClub().TimeZone.Id);
    }
}
