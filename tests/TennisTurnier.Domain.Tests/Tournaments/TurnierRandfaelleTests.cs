using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Tournaments;

/// <summary>
/// Die Ränder des Turnieraggregats: was es beim Anlegen verlangt, was es an
/// Kennungen nicht kennt, und wann ein Zeitpunkt außerhalb liegt.
///
/// Ein Turnier ist die Wurzel (ADR-0009). Eine Kennung, die es nicht kennt,
/// darf deshalb nicht in einer leeren Liste enden, sondern muss als Absage
/// zurückkommen — sonst verschwindet ein Tippfehler lautlos.
/// </summary>
public sealed class TurnierRandfaelleTests
{
    private static Venue Anlage() => new("TC Test", null, "Maria Alm", "Europe/Vienna");

    private static readonly DateOnly Beginn = new(2026, 5, 16);
    private static readonly DateOnly Ende = new(2026, 5, 17);

    private static Tournament Turnier(string name = "Clubmeisterschaft", Guid? vorlage = null) =>
        Turnier(Beginn, Ende, name, vorlage);

    private static Tournament Turnier(
        DateOnly? beginn,
        DateOnly? ende,
        string name = "Clubmeisterschaft",
        Guid? vorlage = null) =>
        new(
            Guid.NewGuid(),
            name,
            Anlage(),
            Discipline.Singles,
            beginn,
            ende,
            vorlage ?? Guid.NewGuid());

    [Fact]
    public void Ein_Turnier_braucht_eine_Formatvorlage()
    {
        // Ohne sie stünde beim Auslosen keine Definition zum Einfrieren bereit —
        // und das fiele erst auf, wenn das Feld schon voll ist.
        var fehler = Assert.Throws<DomainException>(() => Turnier(vorlage: Guid.Empty));

        Assert.Contains("braucht eine Formatvorlage", fehler.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ein_Turnier_braucht_einen_Namen(string name)
    {
        var fehler = Assert.Throws<DomainException>(() => Turnier(name));

        Assert.Contains("braucht einen Namen", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Einen_Platz_den_es_nicht_gibt_kennt_es_nicht()
    {
        var id = Guid.NewGuid();
        var fehler = Assert.Throws<DomainException>(() => Turnier().CourtOf(id));

        Assert.Contains(id.ToString(), fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Meldung_die_es_nicht_gibt_kennt_es_auch_nicht()
    {
        var turnier = Turnier();
        turnier.OpenRegistration();

        var id = Guid.NewGuid();
        var fehler = Assert.Throws<DomainException>(() => turnier.Accept(id));

        Assert.Contains(id.ToString(), fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Einen_vorhandenen_Platz_findet_es_und_benennt_ihn_um()
    {
        var turnier = Turnier();
        var platz = turnier.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

        turnier.RenameCourt(platz.Id, "Center Court");

        Assert.Equal("Center Court", turnier.CourtOf(platz.Id).Name);
    }

    [Fact]
    public void Ein_Platzname_der_schon_vergeben_ist_wird_abgewiesen()
    {
        var turnier = Turnier();
        turnier.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

        // Groß- und Kleinschreibung zählt nicht: „platz 1" und „Platz 1" sind am
        // Aushang derselbe Platz.
        var fehler = Assert.Throws<DomainException>(() =>
            turnier.AddCourt(Guid.NewGuid(), "  platz 1  ", CourtSurface.Clay, CourtLocation.Outdoor));

        Assert.Contains("bereits einen Platz", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_zurueckgezogene_Meldung_haelt_ihre_Setzposition_nicht_besetzt()
    {
        // Sonst bliebe die Eins für den Rest des Turniers gesperrt, weil jemand
        // abgesagt hat.
        var turnier = Turnier();
        turnier.OpenRegistration();

        var erste = turnier.Enter(Guid.NewGuid(), Guid.NewGuid(), seed: 1);
        turnier.Accept(erste.Id);

        var zweite = turnier.Enter(Guid.NewGuid(), Guid.NewGuid());
        turnier.Accept(zweite.Id);

        Assert.Throws<DomainException>(() => turnier.SetSeed(zweite.Id, 1));

        turnier.Withdraw(erste.Id);
        turnier.SetSeed(zweite.Id, 1);

        Assert.Equal(1, zweite.Seed);
    }

    [Fact]
    public void Ohne_Termin_gibt_es_keinen_Zeitraum()
    {
        Assert.Null(Turnier(beginn: null, ende: null).Period());
        Assert.NotNull(Turnier().Period());

        // Ein Ende ohne Beginn ergibt keinen Zeitraum — und wird deshalb schon
        // beim Anlegen abgewiesen, nicht erst beim Rechnen damit.
        Assert.Throws<DomainException>(() => Turnier(beginn: null, ende: Ende));

        // Nur ein Beginn heißt: ein Turniertag. Das Ende zieht nach.
        var eintaegig = Turnier(beginn: Beginn, ende: null);
        Assert.Equal(Beginn, eintaegig.EndsOn);

        // Und ohne Termin gibt es auch keine Schranke, gegen die zu prüfen wäre.
        Turnier(beginn: null, ende: null)
            .RequireScheduledWithin(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Ein_Zeitpunkt_vor_und_nach_dem_Turnier_liegt_ausserhalb()
    {
        var turnier = Turnier();

        // Ein Tag Vorlauf und zwei Tage Nachlauf sind erlaubt: ein Turniertag
        // endet nach Mitternacht, und eine Zeitzone verschiebt die Grenze.
        var davor = new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);
        var danach = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);

        Assert.Throws<DomainException>(() => turnier.RequireScheduledWithin(davor));
        Assert.Throws<DomainException>(() => turnier.RequireScheduledWithin(danach));

        // Und mittendrin geht es.
        turnier.RequireScheduledWithin(new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero));
    }
}
