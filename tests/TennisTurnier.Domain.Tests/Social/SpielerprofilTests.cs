using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Players;

namespace TennisTurnier.Domain.Tests.Social;

/// <summary>
/// Was ein Spieler über sich selbst sagt (ADR-0013).
///
/// Der einzige Teil eines Spielers, den niemand berechnen kann — und deshalb
/// der einzige, für den die Frage zählt, wem er gehört.
/// </summary>
public sealed class SpielerprofilTests
{
    private static Player Spieler() => new(Guid.NewGuid(), "Anna", "Vogel");

    private static Player MitKonto()
    {
        var spieler = Spieler();
        spieler.LinkAccount(Guid.NewGuid());

        return spieler;
    }

    [Fact]
    public void Ein_neuer_Spieler_hat_ein_leeres_Profil()
    {
        var spieler = Spieler();

        Assert.Equal(PlayerProfile.Empty, spieler.Profile);
        Assert.Null(spieler.Profile.Bio);
        Assert.Null(spieler.Profile.HomeClub);
    }

    [Fact]
    public void Wer_ein_Konto_hat_beschreibt_sich_selbst()
    {
        var spieler = MitKonto();

        spieler.Describe(PlayerProfile.From("Spielt seit 2009.", "TC Hinterbrühl"));

        Assert.Equal("Spielt seit 2009.", spieler.Profile.Bio);
        Assert.Equal("TC Hinterbrühl", spieler.Profile.HomeClub);
    }

    /// <summary>
    /// Ohne diese Prüfung könnte die Turnierleitung, die eine Liste eingelesen
    /// hat, den Eingelesenen Sätze in den Mund legen.
    /// </summary>
    [Fact]
    public void Wer_kein_Konto_hat_wird_von_niemandem_beschrieben()
    {
        var spieler = Spieler();

        var fehler = Assert.Throws<DomainException>(
            () => spieler.Describe(PlayerProfile.From("Fremde Worte.", null)));

        Assert.Contains("gehört keinem Konto", fehler.Message);
    }

    [Fact]
    public void Ein_Profil_ohne_Angaben_ist_kein_Fehler()
    {
        var spieler = MitKonto();

        spieler.Describe(PlayerProfile.From("   ", null));

        Assert.Null(spieler.Profile.Bio);
        Assert.Null(spieler.Profile.HomeClub);
    }

    [Fact]
    public void Leerraum_am_Rand_wird_abgeschnitten()
    {
        var profil = PlayerProfile.From("  Spielt gern.  ", "  TC Test  ");

        Assert.Equal("Spielt gern.", profil.Bio);
        Assert.Equal("TC Test", profil.HomeClub);
    }

    [Fact]
    public void Ein_zu_langer_Text_ueber_sich_wird_abgewiesen()
    {
        var fehler = Assert.Throws<DomainException>(
            () => PlayerProfile.From(new string('x', PlayerProfile.MaxBioLength + 1), null));

        Assert.Contains("Der Text über sich", fehler.Message);
    }

    [Fact]
    public void Ein_zu_langer_Heimatverein_wird_abgewiesen()
    {
        var fehler = Assert.Throws<DomainException>(
            () => PlayerProfile.From(null, new string('x', PlayerProfile.MaxHomeClubLength + 1)));

        Assert.Contains("Der Heimatverein", fehler.Message);
    }

    [Fact]
    public void Genau_die_Hoechstlaenge_geht_noch_durch()
    {
        var profil = PlayerProfile.From(
            new string('x', PlayerProfile.MaxBioLength),
            new string('y', PlayerProfile.MaxHomeClubLength));

        Assert.Equal(PlayerProfile.MaxBioLength, profil.Bio!.Length);
        Assert.Equal(PlayerProfile.MaxHomeClubLength, profil.HomeClub!.Length);
    }

    [Fact]
    public void Ein_Profil_laesst_sich_nicht_mit_null_setzen()
    {
        var spieler = MitKonto();

        Assert.Throws<ArgumentNullException>(() => spieler.Describe(null!));
    }

    /// <summary>
    /// Der Anzeigename eines Teilnehmers wird beim Melden festgeschrieben — wer
    /// heiratet, heißt in der Tabelle vom Frühjahr weiterhin, wie er dort
    /// angetreten ist.
    /// </summary>
    [Fact]
    public void Ein_Spieler_laesst_sich_umbenennen()
    {
        var spieler = Spieler();

        spieler.Rename("Anna Maria", "Vogel-Berger");

        Assert.Equal("Vogel-Berger, Anna Maria", spieler.DisplayName);
    }

    [Theory]
    [InlineData("", "Vogel")]
    [InlineData("   ", "Vogel")]
    [InlineData("Anna", "")]
    [InlineData("Anna", "   ")]
    public void Ein_leerer_Name_wird_abgewiesen(string vorname, string nachname)
    {
        var spieler = Spieler();

        Assert.Throws<DomainException>(() => spieler.Rename(vorname, nachname));
    }
}
