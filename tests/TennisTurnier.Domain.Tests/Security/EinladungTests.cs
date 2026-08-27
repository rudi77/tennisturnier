using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Domain.Tests.Security;

/// <summary>
/// Die Einladung an eine Adresse, zu der es noch kein Konto gibt.
///
/// Sie ist der Weg, den ADR-0007 skizziert hat: eine Vorabzuweisung, eingelöst
/// beim ersten Login. Was sie zusagt, ist wenig — eine Adresse, eine Rolle, ein
/// Turnier —, und genau deshalb muss jedes der drei stimmen.
/// </summary>
public sealed class EinladungTests
{
    private static readonly DateTimeOffset Jetzt =
        new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Eine_Einladung_gehoert_zu_einem_Turnier()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            new Invitation(Guid.NewGuid(), Guid.Empty, "anna@verein.at", Role.Member, Jetzt));

        Assert.Contains("gehört zu einem Turnier", fehler.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ohne_Adresse_gibt_es_nichts_einzuloesen(string keine)
    {
        var fehler = Assert.Throws<DomainException>(() =>
            new Invitation(Guid.NewGuid(), Guid.NewGuid(), keine, Role.Member, Jetzt));

        Assert.Contains("E-Mail-Adresse", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Die_Adresse_wird_kleingeschrieben_gespeichert()
    {
        // Sie ist der Schlüssel zum Einlösen. „Anna@Verein.at" und
        // „anna@verein.at" sind derselbe Mensch; stünde beides nebeneinander,
        // bekäme er seine Rolle je nach Schreibweise seines Ausstellers.
        var einladung = new Invitation(
            Guid.NewGuid(), Guid.NewGuid(), "  Anna@Verein.AT  ", Role.Referee, Jetzt);

        Assert.Equal("anna@verein.at", einladung.Email);
    }

    [Fact]
    public void Eingeloest_wird_sie_zu_einer_Rollenzuweisung_im_Turnierscope()
    {
        var turnier = Guid.NewGuid();
        var konto = Guid.NewGuid();

        var zuweisung = new Invitation(Guid.NewGuid(), turnier, "anna@verein.at", Role.Referee, Jetzt)
            .Redeem(konto);

        Assert.Equal(konto, zuweisung.UserId);
        Assert.Equal(Role.Referee, zuweisung.Role);
        Assert.Equal(ResourceScope.Tournament(turnier), zuweisung.Scope);
    }

    [Fact]
    public void Der_Zeitpunkt_bleibt_stehen()
    {
        // Ohne Verfall ist er keine Frist, sondern eine Auskunft: „vor drei
        // Monaten eingeladen, nie gekommen" ist der Grund, sie zurückzunehmen.
        var einladung = new Invitation(
            Guid.NewGuid(), Guid.NewGuid(), "anna@verein.at", Role.Member, Jetzt);

        Assert.Equal(Jetzt, einladung.CreatedAt);
    }
}
