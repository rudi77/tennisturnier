using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;

namespace TennisTurnier.Domain.Tests.Phases;

/// <summary>
/// Die Phase als Aggregat: was sie beim Anlegen verlangt, wie sie einen
/// K.-o.-Baum verdrahtet und was sie tut, wenn der Baum nicht die Form hat,
/// die sie erwartet.
///
/// Die Randfälle sind hier keine Spitzfindigkeit. Ein Baum entsteht aus dem,
/// was ein Format liefert, und ein Format ist austauschbar (ADR-0001) — die
/// Phase muss also auch mit einer Struktur zurechtkommen, die sie nicht selbst
/// erzeugt hat, ohne dabei falsche Abhängigkeiten zu verdrahten.
/// </summary>
public sealed class PhaseTests
{
    private static readonly Guid Turnier = Guid.NewGuid();

    private static Phase Bauen(PhaseFormatKind format = PhaseFormatKind.Knockout, string? name = null) =>
        new(Guid.NewGuid(), Turnier, 1, format, name);

    private static ParticipantRef Meldung() => ParticipantRef.Of(Guid.NewGuid());

    private static Score Ergebnis() =>
        Score.Played([new SetScore(6, 4), new SetScore(6, 3)], new MatchFormat());

    [Fact]
    public void Eine_Phase_braucht_ein_Turnier()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            new Phase(Guid.NewGuid(), Guid.Empty, 1, PhaseFormatKind.Knockout));

        Assert.Contains("braucht ein Turnier", fehler.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Eine_Phase_beginnt_bei_eins(int ordinal)
    {
        var fehler = Assert.Throws<DomainException>(() =>
            new Phase(Guid.NewGuid(), Turnier, ordinal, PhaseFormatKind.Knockout));

        Assert.Contains($"war {ordinal}", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Namen_heisst_die_Phase_wie_ihr_Format()
    {
        Assert.Equal("Knockout", Bauen().Name);
        Assert.Equal("Knockout", Bauen(name: "   ").Name);
        Assert.Equal("Endrunde", Bauen(name: "  Endrunde  ").Name);
    }

    [Fact]
    public void Eine_leere_Phase_wartet_noch()
    {
        Assert.Equal(PhaseStatus.Pending, Bauen().Status);
    }

    [Fact]
    public void Verlangt_Paarungen()
    {
        Assert.Throws<ArgumentNullException>(() => Bauen().AddPairings(null!));
    }

    [Fact]
    public void Verdrahtet_die_Runden_eines_Baums()
    {
        var phase = Bauen();

        var matches = phase.AddPairings([
            new Pairing(1, 1, Meldung(), Meldung(), "HF1"),
            new Pairing(1, 2, Meldung(), Meldung(), "HF2"),
            new Pairing(2, 1, ParticipantRef.Open, ParticipantRef.Open, "F"),
        ]);

        var finale = matches.Single(m => m.Label == "F");
        Assert.Equal(matches[0].Id, finale.Side1.Origin.DependsOnMatch);
        Assert.Equal(matches[1].Id, finale.Side2.Origin.DependsOnMatch);
    }

    [Fact]
    public void Verdrahtet_nichts_wo_die_Vorrunde_fehlt()
    {
        // Runde 2 ohne Runde 1: kein Format erzeugt das, aber die Phase darf
        // daran nicht scheitern — sie ließe sonst das ganze Turnier stehen.
        var phase = Bauen();

        var matches = phase.AddPairings([
            new Pairing(2, 1, ParticipantRef.Open, ParticipantRef.Open, "F"),
        ]);

        Assert.Null(matches[0].Side1.Origin.DependsOnMatch);
    }

    [Fact]
    public void Verdrahtet_das_Spiel_um_Platz_drei_mit_den_Verlierern_der_Halbfinals()
    {
        var phase = Bauen();

        var matches = phase.AddPairings([
            new Pairing(1, 1, Meldung(), Meldung(), "HF1"),
            new Pairing(1, 2, Meldung(), Meldung(), "HF2"),
            new Pairing(2, 1, ParticipantRef.Open, ParticipantRef.Open, "F"),
            new Pairing(2, 2, ParticipantRef.Open, ParticipantRef.Open, KnockoutFormat.ThirdPlaceLabel),
        ]);

        var platzDrei = matches.Single(m => m.Label == KnockoutFormat.ThirdPlaceLabel);
        Assert.IsType<ParticipantRef.LoserOf>(platzDrei.Side1.Origin);
        Assert.Equal(matches[0].Id, platzDrei.Side1.Origin.DependsOnMatch);
        Assert.Equal(matches[1].Id, platzDrei.Side2.Origin.DependsOnMatch);
    }

    [Fact]
    public void Laesst_das_Spiel_um_Platz_drei_offen_wo_es_keine_zwei_Halbfinals_gibt()
    {
        var phase = Bauen();

        var matches = phase.AddPairings([
            new Pairing(1, 1, Meldung(), Meldung(), "HF1"),
            new Pairing(2, 1, ParticipantRef.Open, ParticipantRef.Open, "F"),
            new Pairing(2, 2, ParticipantRef.Open, ParticipantRef.Open, KnockoutFormat.ThirdPlaceLabel),
        ]);

        var platzDrei = matches.Single(m => m.Label == KnockoutFormat.ThirdPlaceLabel);
        Assert.Null(platzDrei.Side1.Origin.DependsOnMatch);
    }

    [Fact]
    public void Verdrahtet_ausserhalb_eines_KO_Baums_gar_nichts()
    {
        var phase = Bauen(PhaseFormatKind.RoundRobin);

        var matches = phase.AddPairings([
            new Pairing(1, 1, Meldung(), Meldung(), null, "A"),
            new Pairing(2, 1, ParticipantRef.Open, ParticipantRef.Open, null, "A"),
        ]);

        Assert.Null(matches[1].Side1.Origin.DependsOnMatch);
    }

    [Fact]
    public void Ein_Freilos_entscheidet_sich_erst_wenn_der_Gegner_feststeht()
    {
        var phase = Bauen();

        // Runde 1 offen gegen Freilos: der Gegner steht noch nicht fest, also
        // ist auch das Freilos noch nicht entschieden.
        var matches = phase.AddPairings([
            new Pairing(1, 1, ParticipantRef.FromGroupPosition(Guid.NewGuid(), "A", 1), ParticipantRef.ByeSlot),
        ]);

        Assert.Null(matches[0].Score);
        Assert.Equal(MatchStatus.Pending, matches[0].Status);
    }

    [Fact]
    public void Ein_Freilos_mit_feststehendem_Gegner_ist_sofort_entschieden()
    {
        var phase = Bauen();
        var meldung = Guid.NewGuid();

        var matches = phase.AddPairings([
            new Pairing(1, 1, ParticipantRef.Of(meldung), ParticipantRef.ByeSlot),
        ]);

        Assert.Equal(MatchOutcome.Bye, matches[0].Score?.Outcome);
        Assert.Equal(meldung, matches[0].WinnerEntryId);
    }

    [Fact]
    public void Ein_Freilos_auf_der_ersten_Seite_traegt_die_zweite_weiter()
    {
        var phase = Bauen();
        var meldung = Guid.NewGuid();

        var matches = phase.AddPairings([
            new Pairing(1, 1, ParticipantRef.ByeSlot, ParticipantRef.Of(meldung)),
        ]);

        Assert.Equal(meldung, matches[0].WinnerEntryId);
    }

    [Fact]
    public void Ein_Ergebnis_zurueckzunehmen_das_es_nicht_gibt_aendert_nichts()
    {
        var phase = Bauen();
        var matches = phase.AddPairings([new Pairing(1, 1, Meldung(), Meldung())]);

        phase.ClearResult(matches[0].Id);

        Assert.Null(matches[0].Score);
    }

    [Fact]
    public void Ein_unbekanntes_Match_wird_benannt()
    {
        var phase = Bauen();
        var id = Guid.NewGuid();

        var fehler = Assert.Throws<DomainException>(() => phase.ClearResult(id));

        Assert.Contains(id.ToString(), fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Ruecknahme_leert_beide_abhaengigen_Seiten()
    {
        var phase = Bauen();
        var matches = phase.AddPairings([
            new Pairing(1, 1, Meldung(), Meldung(), "HF1"),
            new Pairing(1, 2, Meldung(), Meldung(), "HF2"),
            new Pairing(2, 1, ParticipantRef.Open, ParticipantRef.Open, "F"),
        ]);

        phase.RecordResult(matches[0].Id, Ergebnis());
        phase.RecordResult(matches[1].Id, Ergebnis());

        var finale = matches[2];
        Assert.NotNull(finale.Side1.EntryId);
        Assert.NotNull(finale.Side2.EntryId);

        phase.ClearResult(matches[0].Id);
        Assert.Null(finale.Side1.EntryId);

        phase.ClearResult(matches[1].Id);
        Assert.Null(finale.Side2.EntryId);

        // Die Herkunft bleibt: „Sieger aus HF1" gilt weiter, nur ist der Sieger
        // wieder offen.
        Assert.Equal(matches[0].Id, finale.Side1.Origin.DependsOnMatch);
    }

    [Fact]
    public void Eine_Phase_ist_fertig_wenn_jedes_Match_entschieden_ist()
    {
        var phase = Bauen();
        var matches = phase.AddPairings([new Pairing(1, 1, Meldung(), Meldung())]);
        Assert.Equal(PhaseStatus.Running, phase.Status);

        phase.RecordResult(matches[0].Id, Ergebnis());

        Assert.Equal(PhaseStatus.Completed, phase.Status);
        Assert.True(phase.HasAnyResult);
    }
}
