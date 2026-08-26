using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Players;

namespace TennisTurnier.Domain.Tests.Tournaments;

/// <summary>
/// Der Spieler und sein Konto.
///
/// Bis hierher waren es zwei getrennte Welten: Konten wussten nichts von
/// Spielern, Spieler nichts von Konten. Wer über einen Beitrittslink
/// mitspielt, verbindet beide — und wer aus einer hochgeladenen Liste kommt,
/// bleibt ohne. Beides muss gelten, sonst ist entweder die Selbstmeldung
/// unmöglich oder die Liste.
/// </summary>
public sealed class SpielerKontoTests
{
    [Fact]
    public void Ein_frischer_Spieler_gehoert_niemandem()
    {
        var spieler = new Player(Guid.NewGuid(), "Anna", "Müller");

        Assert.Null(spieler.UserAccountId);
    }

    [Fact]
    public void Ein_Beitritt_verbindet_ihn_mit_seinem_Konto()
    {
        var konto = Guid.NewGuid();
        var spieler = new Player(Guid.NewGuid(), "Anna", "Müller");

        spieler.LinkAccount(konto);

        Assert.Equal(konto, spieler.UserAccountId);
    }

    [Fact]
    public void Derselbe_Beitritt_ein_zweites_Mal_ist_kein_Fehler()
    {
        // Es ist derselbe Mensch, der einem zweiten Turnier beitritt. Ein
        // Fehler hier hieße, dass man nur einmal im Leben mitspielen darf.
        var konto = Guid.NewGuid();
        var spieler = new Player(Guid.NewGuid(), "Anna", "Müller");

        spieler.LinkAccount(konto);
        spieler.LinkAccount(konto);

        Assert.Equal(konto, spieler.UserAccountId);
    }

    [Fact]
    public void Ein_zweites_Konto_am_selben_Spieler_wird_bemerkt()
    {
        // Entweder steht ein Namensvetter unter fremder Adresse, oder zwei
        // Menschen teilen sich einen Spieler. Beides will bemerkt und nicht
        // stillschweigend überschrieben werden.
        var spieler = new Player(Guid.NewGuid(), "Anna", "Müller");
        spieler.LinkAccount(Guid.NewGuid());

        var fehler = Assert.Throws<DomainException>(() => spieler.LinkAccount(Guid.NewGuid()));

        Assert.Contains("bereits einem anderen Konto", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_leeres_Konto_ist_keines()
    {
        var spieler = new Player(Guid.NewGuid(), "Anna", "Müller");

        var fehler = Assert.Throws<DomainException>(() => spieler.LinkAccount(Guid.Empty));

        Assert.Contains("leeren Konto", fehler.Message, StringComparison.Ordinal);
    }
}
