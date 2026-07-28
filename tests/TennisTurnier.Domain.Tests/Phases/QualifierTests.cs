using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;

namespace TennisTurnier.Domain.Tests.Phases;

/// <summary>
/// Wer aus einer Vorphase weiterkommt und auf welchem Startplatz.
///
/// Der Kern ist die Überkreuzung: sie zeigt sich erst im Zusammenspiel mit der
/// Setzlistenverteilung des K.-o.-Baums, weshalb die Tests hier die Bracketpaare
/// nachrechnen statt nur die Reihenfolge zu prüfen.
/// </summary>
public sealed class QualifierTests
{
    private static readonly Guid SourcePhase = Guid.NewGuid();

    private static PhaseDefinition Source(int groupCount) => new()
    {
        Ordinal = 1,
        Format = PhaseFormatKind.RoundRobin,
        GroupCount = groupCount,
    };

    private static IReadOnlyList<SeededEntry> Entries(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new SeededEntry(Guid.NewGuid(), i, $"Spielerin {i:00}"))];

    private static IReadOnlyList<SeededEntry> Slots(
        int groupCount,
        int participants,
        QualificationRule rule = QualificationRule.TopNPerGroup,
        int n = 2) =>
        Qualifier.Slots(
            new Qualification(FromPhase: 1, rule, n),
            SourcePhase,
            Source(groupCount),
            Entries(participants));

    private static (string Group, int Rank) PositionOf(SeededEntry slot)
    {
        var position = Assert.IsType<ParticipantRef.GroupPosition>(slot.Origin);
        return (position.Group, position.Rank);
    }

    [Fact]
    public void Die_besten_Zwei_von_vier_Gruppen_ergeben_acht_Startplaetze()
    {
        var slots = Slots(groupCount: 4, participants: 16);

        Assert.Equal(8, slots.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], slots.Select(s => s.Seed));
        Assert.All(slots, slot => Assert.False(slot.IsSettled));
    }

    [Fact]
    public void Zuerst_die_Gruppensieger_dann_die_Zweiten()
    {
        var slots = Slots(groupCount: 4, participants: 16);

        Assert.Equal(
            [("Gruppe A", 1), ("Gruppe B", 1), ("Gruppe C", 1), ("Gruppe D", 1)],
            slots.Take(4).Select(PositionOf));

        Assert.Equal(
            [("Gruppe A", 2), ("Gruppe B", 2), ("Gruppe C", 2), ("Gruppe D", 2)],
            slots.Skip(4).Select(PositionOf));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void Kein_Bracketpaar_kommt_aus_derselben_Gruppe(int groupCount)
    {
        // Die eigentliche Behauptung, und sie hängt an der Setzlistenverteilung:
        // Setzung i trifft auf Setzung 2G+1−i. Läuft die Zuordnung falsch herum,
        // spielt der Sieger einer Gruppe sofort wieder gegen ihren Zweiten.
        var slots = Slots(groupCount, participants: groupCount * 4);
        var order = KnockoutFormat.SeedOrder(KnockoutFormat.NextPowerOfTwo(slots.Count));

        for (var position = 0; position < order.Count / 2; position++)
        {
            var one = slots[order[position * 2] - 1];
            var two = slots[order[(position * 2) + 1] - 1];

            Assert.NotEqual(PositionOf(one).Group, PositionOf(two).Group);
        }
    }

    [Fact]
    public void Bei_ungerader_Gruppenzahl_wird_die_eine_Kollision_aufgeloest()
    {
        // Bei drei Gruppen ginge die Rechnung für die mittlere nicht auf: ihr
        // Sieger träfe auf ihren eigenen Zweiten.
        var slots = Slots(groupCount: 3, participants: 12);

        Assert.Equal(
            [("Gruppe A", 2), ("Gruppe C", 2), ("Gruppe B", 2)],
            slots.Skip(3).Select(PositionOf));
    }

    [Fact]
    public void Beste_Dritte_fuellen_auf_die_naechste_Zweierpotenz_auf()
    {
        // Sechs Gruppen, die besten Zwei — das sind zwölf. Aufgefüllt wird auf
        // sechzehn, also mit den vier besten Dritten.
        var slots = Slots(groupCount: 6, participants: 24, QualificationRule.BestThirds);

        Assert.Equal(16, slots.Count);
        Assert.Equal(
            [("#bester", 3), ("#zweitbester", 3), ("#drittbester", 3), ("#4.-bester", 3)],
            slots.Skip(12).Select(PositionOf));

        Assert.Equal("bester Dritter", slots[12].DisplayName);
    }

    [Fact]
    public void Ohne_Auffuellbedarf_bleibt_es_bei_den_direkten_Plaetzen()
    {
        var slots = Slots(groupCount: 4, participants: 16, QualificationRule.BestThirds);

        Assert.Equal(8, slots.Count);
        Assert.DoesNotContain(slots, slot => Qualifier.IsBestOfRank(PositionOf(slot).Group));
    }

    [Fact]
    public void Ein_Platz_der_in_keiner_Gruppe_existiert_wird_nicht_vergeben()
    {
        // Fünf Teilnehmer in zwei Gruppen: drei und zwei. Der Platz „Dritter der
        // kleineren Gruppe" bliebe für immer unbesetzt — und mit ihm das Match,
        // in dem er stünde.
        var slots = Slots(groupCount: 2, participants: 5, QualificationRule.All);

        Assert.Equal(5, slots.Count);
        Assert.Single(slots, slot => PositionOf(slot).Rank == 3);
    }

    [Fact]
    public void Die_Bezeichnung_eines_Startplatzes_ist_lesbar()
    {
        var slots = Slots(groupCount: 4, participants: 16);

        Assert.Equal("Erster der Gruppe A", slots[0].DisplayName);
        Assert.Equal("Zweiter der Gruppe A", slots[4].DisplayName);
    }

    [Fact]
    public void Eine_Vorphase_ohne_Gruppen_liefert_Plaetze_ihrer_Tabelle()
    {
        // Regression: „Liga, danach Halbfinale und Finale" ist eine ganz normale
        // Ausschreibung. Sie scheiterte beim Auslosen, weil eine Phase mit einer
        // einzigen Gruppe bewusst keinen Gruppennamen trägt und die
        // Gruppenplatzreferenz einen verlangte.
        var slots = Slots(groupCount: 1, participants: 8, n: 4);

        Assert.Equal(4, slots.Count);
        Assert.Equal([("", 1), ("", 2), ("", 3), ("", 4)], slots.Select(PositionOf));
        Assert.Equal("Erster der Tabelle", slots[0].DisplayName);
    }

    [Fact]
    public void Es_entstehen_nur_so_viele_Auffuellplaetze_wie_es_Kandidaten_gibt()
    {
        // Regression: die Auffüllplätze wurden nur an der Gruppenzahl bemessen,
        // nicht daran, wie viele Gruppen den betreffenden Rang überhaupt haben.
        // Bei ungleich großen Gruppen entstand ein Platz ohne möglichen
        // Kandidaten — und das Match dahinter blieb für immer offen.
        var slots = Slots(groupCount: 3, participants: 7, QualificationRule.BestThirds);

        Assert.Equal(7, slots.Count);
        Assert.Single(slots, slot => Qualifier.IsBestOfRank(PositionOf(slot).Group));
    }
}
