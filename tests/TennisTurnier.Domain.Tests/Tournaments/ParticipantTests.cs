using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Tournaments;

public sealed class ParticipantTests
{
    [Fact]
    public void Ein_Einzelteilnehmer_hat_genau_einen_Spieler()
    {
        var playerId = Guid.NewGuid();

        var participant = Participant.Single(Guid.NewGuid(), playerId, "Müller, Anna");

        Assert.False(participant.IsTeam);
        Assert.Equal([playerId], participant.PlayerIds);
    }

    [Fact]
    public void Ein_Doppel_hat_zwei_Spieler()
    {
        var participant = Participant.Team(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Müller / Berger");

        Assert.True(participant.IsTeam);
        Assert.Equal(2, participant.PlayerIds.Count);
    }

    [Fact]
    public void Ein_Doppel_braucht_zwei_verschiedene_Spieler()
    {
        var playerId = Guid.NewGuid();

        Assert.Throws<DomainException>(
            () => Participant.Team(Guid.NewGuid(), playerId, playerId, "Müller / Müller"));
    }

    [Fact]
    public void Ein_Teilnehmer_braucht_einen_Anzeigenamen()
    {
        Assert.Throws<DomainException>(() => Participant.Single(Guid.NewGuid(), Guid.NewGuid(), "  "));
    }

    [Fact]
    public void Ein_zu_langer_Anzeigename_wird_abgewiesen()
    {
        // SQLite setzt die Längenangabe der Spalte nicht durch (ADR-0006). Ohne
        // diese Prüfung ginge ein überlanger Teamname hier still durch und
        // scheiterte erst auf einer Datenbank, die es genauer nimmt.
        var zuLang = new string('x', 201);

        var fehler = Assert.Throws<DomainException>(
            () => Participant.Single(Guid.NewGuid(), Guid.NewGuid(), zuLang));

        Assert.Contains("200", fehler.Message);
    }

    [Fact]
    public void Genau_die_Hoechstlaenge_ist_noch_erlaubt()
    {
        var gerade_noch = new string('x', 200);

        var participant = Participant.Single(Guid.NewGuid(), Guid.NewGuid(), gerade_noch);

        Assert.Equal(gerade_noch, participant.DisplayName);
    }

    [Fact]
    public void Ein_Einzelteilnehmer_braucht_einen_Spieler()
    {
        Assert.Throws<DomainException>(() => Participant.Single(Guid.NewGuid(), Guid.Empty, "Müller, Anna"));
    }

    [Fact]
    public void Gemeinsame_Spieler_werden_erkannt()
    {
        // Der Grund für diese Frage steht im Spielplan: wer im Einzel und im
        // Doppel gemeldet ist, kann nicht zeitgleich auf zwei Plätzen stehen.
        var anna = Guid.NewGuid();
        var einzel = Participant.Single(Guid.NewGuid(), anna, "Müller, Anna");
        var doppel = Participant.Team(Guid.NewGuid(), anna, Guid.NewGuid(), "Müller / Berger");
        var fremd = Participant.Single(Guid.NewGuid(), Guid.NewGuid(), "Huber, Eva");

        Assert.True(einzel.SharesPlayerWith(doppel));
        Assert.True(doppel.SharesPlayerWith(einzel));
        Assert.False(einzel.SharesPlayerWith(fremd));
    }

    [Fact]
    public void Der_Anzeigename_eines_Spielers_ist_Nachname_Komma_Vorname()
    {
        var player = new Player(Guid.NewGuid(), "Anna", "Müller");

        Assert.Equal("Müller, Anna", player.DisplayName);
    }

    [Fact]
    public void Ein_Spieler_braucht_Vor_und_Nachname()
    {
        Assert.Throws<DomainException>(() => new Player(Guid.NewGuid(), " ", "Müller"));
        Assert.Throws<DomainException>(() => new Player(Guid.NewGuid(), "Anna", ""));
    }

    [Fact]
    public void Ein_Spieler_ohne_Kontaktdaten_hat_ein_leeres_Buendel_statt_null()
    {
        var player = new Player(Guid.NewGuid(), "Anna", "Müller");

        Assert.Equal(PlayerContact.Empty, player.Contact);
        Assert.Null(player.Contact.Email);
    }
}
