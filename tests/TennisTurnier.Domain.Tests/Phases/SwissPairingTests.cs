using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Phases;

namespace TennisTurnier.Domain.Tests.Phases;

/// <summary>
/// Die Paarung einer Schweizer Runde als reine Rechnung — ohne Phase, ohne
/// Matches.
///
/// Hier stehen die Fälle, die ein normaler Turnierverlauf nicht erzeugt: ein
/// Feld, in dem jeder schon gegen jeden gespielt hat, und eines, in dem eine
/// wiederholungsfreie Paarung gar nicht existiert. Beides muss eine Antwort
/// ergeben — am Turniertag ist eine Runde mit einer Wiederholung immer noch
/// besser als eine Runde, die nicht zustande kommt.
/// </summary>
public sealed class SwissPairingTests
{
    private static IReadOnlyList<Guid> Feld(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => Guid.NewGuid())];

    private static IReadOnlyDictionary<Guid, int> Punktgleich(IReadOnlyList<Guid> feld) =>
        feld.ToDictionary(id => id, _ => 0);

    private static IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> Begegnungen(
        params (Guid Einer, IEnumerable<Guid> Gegner)[] paare) =>
        paare.ToDictionary(p => p.Einer, p => (IReadOnlySet<Guid>)p.Gegner.ToHashSet());

    [Fact]
    public void Ein_gerades_Feld_bekommt_kein_Freilos()
    {
        var feld = Feld(4);

        Assert.Null(SwissPairing.PickBye(feld, new HashSet<Guid>()));
    }

    [Fact]
    public void Das_Freilos_geht_an_den_Letzten_der_noch_keines_hatte()
    {
        var feld = Feld(5);

        Assert.Equal(feld[4], SwissPairing.PickBye(feld, new HashSet<Guid>()));
        Assert.Equal(feld[3], SwissPairing.PickBye(feld, new HashSet<Guid> { feld[4] }));
    }

    [Fact]
    public void Ein_zweites_Freilos_gibt_es_nicht()
    {
        // Mehr Runden als Spieler: irgendwann hat jeder ausgesetzt, und ein
        // geschenkter zweiter Punkt entschiede das Turnier.
        var feld = Feld(3);

        var fehler = Assert.Throws<DomainException>(() =>
            SwissPairing.PickBye(feld, feld.ToHashSet()));

        Assert.Contains("kein Spieler übrig", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Runde_paart_eine_gerade_Anzahl()
    {
        var feld = Feld(3);

        var fehler = Assert.Throws<DomainException>(() =>
            SwissPairing.PairRound(feld, Punktgleich(feld), Begegnungen()));

        Assert.Contains("waren 3", fehler.Message, StringComparison.Ordinal);
        Assert.Contains("Freilos wird vorher vergeben", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wo_nur_noch_eine_Wiederholung_bleibt_wird_sie_ausgewiesen()
    {
        // Zwei Spieler, die schon gegeneinander gespielt haben. Die Runde kommt
        // zustande — aber sie ist als Wiederholung gekennzeichnet, damit die
        // Turnierleitung es sieht und nicht erst am Platz erfährt.
        var feld = Feld(2);

        var runde = SwissPairing.PairRound(
            feld,
            Punktgleich(feld),
            Begegnungen((feld[0], [feld[1]]), (feld[1], [feld[0]])));

        var paar = Assert.Single(runde);
        Assert.True(paar.Rematch);
        Assert.Equal(feld.ToHashSet(), new[] { paar.Side1, paar.Side2 }.ToHashSet());
    }

    [Fact]
    public void Was_in_der_Punktgruppe_scheitert_gelingt_ueber_das_ganze_Feld()
    {
        // Die beiden Letzten haben schon gegeneinander gespielt. Punktgruppe für
        // Punktgruppe geht die Runde deshalb nicht auf — über das ganze Feld
        // hinweg sehr wohl, und zwar ohne eine einzige Wiederholung.
        var feld = Feld(4);
        var punkte = new Dictionary<Guid, int>
        {
            [feld[0]] = 2,
            [feld[1]] = 2,
            [feld[2]] = 0,
            [feld[3]] = 0,
        };

        var runde = SwissPairing.PairRound(
            feld,
            punkte,
            Begegnungen((feld[2], [feld[3]]), (feld[3], [feld[2]])));

        Assert.Equal(2, runde.Count);
        Assert.All(runde, paar => Assert.False(paar.Rematch));
    }

    [Fact]
    public void Eine_aussichtslose_Suche_gibt_auf_statt_weiterzusuchen()
    {
        // Dreizehn Spieler, die untereinander alle schon gespielt haben, und elf,
        // die für sie als Gegner in Frage kämen: eine wiederholungsfreie Paarung
        // gibt es nicht, und der Beweis dafür wäre eine Suche über Millionen
        // Möglichkeiten. Sie bricht ab, und der letzte Anlauf paart mit
        // Wiederholungen — am Turniertag wartet sonst niemand mehr.
        var verbunden = Feld(13);
        var frei = Feld(11);
        var feld = verbunden.Concat(frei).ToList();

        var begegnungen = verbunden.ToDictionary(
            id => id,
            id => (IReadOnlySet<Guid>)verbunden.Where(other => other != id).ToHashSet());

        var runde = SwissPairing.PairRound(feld, Punktgleich(feld), begegnungen);

        Assert.Equal(12, runde.Count);
        Assert.Contains(runde, paar => paar.Rematch);

        // Und jeder kommt genau einmal vor — eine Paarung bleibt eine Paarung.
        var gepaart = runde.SelectMany(paar => new[] { paar.Side1, paar.Side2 }).ToList();
        Assert.Equal(feld.Count, gepaart.Distinct().Count());
    }
}
