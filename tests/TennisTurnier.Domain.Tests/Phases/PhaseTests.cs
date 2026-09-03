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

    /// <summary>
    /// Ein Freilos ist kein eingetragenes Ergebnis.
    ///
    /// An dieser Frage hängt, ob eine Vorphase noch angetastet werden darf.
    /// Zählte das Freilos mit, wäre eine Endrunde mit Freilosen ab dem Moment
    /// gesperrt, in dem die Gruppen fertig sind — und kein Gruppenergebnis
    /// mehr zu korrigieren, obwohl in der Endrunde kein Ball gespielt wurde.
    /// </summary>
    [Fact]
    public void Ein_Freilos_zaehlt_nicht_als_eingetragenes_Ergebnis()
    {
        var phase = Bauen();

        var matches = phase.AddPairings([
            new Pairing(1, 1, Meldung(), ParticipantRef.ByeSlot, "VF1"),
            new Pairing(1, 2, Meldung(), Meldung(), "VF2"),
        ]);

        // Das Freilos ist sofort entschieden — ohne dass jemand gespielt hätte.
        Assert.NotNull(matches[0].Score);
        Assert.False(phase.HasAnyResult);

        phase.RecordResult(matches[1].Id, Ergebnis());

        Assert.True(phase.HasAnyResult);
    }

    /// <summary>
    /// Eine korrigierte Gruppentabelle besetzt auch ein Freilos-Match neu.
    ///
    /// Der Freilos-Spielstand steht dem im Weg — ein entschiedenes Match lässt
    /// sich nicht umbesetzen, und das ist überall sonst richtig. Hier ist der
    /// Spielstand aber selbst eine Folge davon, wer im Baum steht, und genau
    /// das ändert sich gerade. Bliebe er stehen, behielte die Endrunde den
    /// Qualifikanten aus der alten Tabelle.
    /// </summary>
    [Fact]
    public void Eine_korrigierte_Gruppentabelle_besetzt_auch_ein_Freilos_neu()
    {
        var vorphase = Guid.NewGuid();
        var phase = Bauen();

        var matches = phase.AddPairings([
            new Pairing(
                1, 1, ParticipantRef.FromGroupPosition(vorphase, "A", 1), ParticipantRef.ByeSlot, "VF1"),
        ]);

        var erster = Guid.NewGuid();
        Assert.True(phase.ResolveGroupPositions(vorphase, new Dictionary<(string, int), Guid>
        {
            [("A", 1)] = erster,
        }));

        Assert.Equal(erster, matches[0].Side1.EntryId);
        Assert.Equal(erster, matches[0].WinnerEntryId);

        // Die Gruppe wird korrigiert, ein anderer ist qualifiziert.
        var zweiter = Guid.NewGuid();
        Assert.True(phase.ResolveGroupPositions(vorphase, new Dictionary<(string, int), Guid>
        {
            [("A", 1)] = zweiter,
        }));

        Assert.Equal(zweiter, matches[0].Side1.EntryId);
        Assert.Equal(zweiter, matches[0].WinnerEntryId);
    }

    [Fact]
    public void Dieselbe_Gruppentabelle_ruehrt_das_Freilos_nicht_an()
    {
        // Die Gegenprobe: ohne Änderung darf der Spielstand nicht weichen und
        // wieder entstehen. Er zählte sonst bei jedem Aufruf die Version des
        // Matches hoch und erzeugte einen Schreibvorgang für nichts.
        var vorphase = Guid.NewGuid();
        var phase = Bauen();

        var matches = phase.AddPairings([
            new Pairing(
                1, 1, ParticipantRef.FromGroupPosition(vorphase, "A", 1), ParticipantRef.ByeSlot, "VF1"),
        ]);

        var qualifiziert = new Dictionary<(string, int), Guid> { [("A", 1)] = Guid.NewGuid() };

        Assert.True(phase.ResolveGroupPositions(vorphase, qualifiziert));
        var version = matches[0].Version;

        Assert.False(phase.ResolveGroupPositions(vorphase, qualifiziert));
        Assert.Equal(version, matches[0].Version);
    }

    [Fact]
    public void Eine_Referenz_auf_ein_fremdes_Match_bleibt_offen()
    {
        // Kein Format erzeugt das — aber ein Format ist austauschbar (ADR-0001),
        // und eine Referenz ins Leere darf die Phase nicht zum Absturz bringen.
        var phase = Bauen(PhaseFormatKind.RoundRobin);

        var matches = phase.AddPairings([
            new Pairing(1, 1, ParticipantRef.FromWinnerOf(Guid.NewGuid()), Meldung(), null, "A"),
        ]);

        Assert.False(matches[0].Side1.Origin.IsResolved);
    }

    [Fact]
    public void Ein_Spiel_um_Platz_drei_ohne_Vorrunde_bleibt_offen()
    {
        // Ein Finale in Runde eins gibt es nicht — es sei denn, ein anderes
        // Format legt es so an. Dann fehlen die Halbfinals, und das Spiel um
        // Platz drei bleibt unverdrahtet, statt in die Liste zu greifen.
        var phase = Bauen();

        var matches = phase.AddPairings([
            new Pairing(1, 1, Meldung(), Meldung(), "F"),
            new Pairing(1, 2, ParticipantRef.Open, ParticipantRef.Open, KnockoutFormat.ThirdPlaceLabel),
        ]);

        var platzDrei = matches.Single(m => m.Label == KnockoutFormat.ThirdPlaceLabel);
        Assert.Null(platzDrei.Side1.Origin.DependsOnMatch);
    }

    [Fact]
    public void Ein_Format_ohne_Implementierung_wird_benannt()
    {
        // Aus der Ablage kann eine Formatart kommen, die diese Fassung nicht
        // kennt. Die Absage nennt den Weg, sie nachzuliefern.
        Assert.False(PhaseFormats.IsSupported((PhaseFormatKind)99));

        var fehler = Assert.Throws<DomainException>(() => PhaseFormats.For((PhaseFormatKind)99));

        Assert.Contains("noch keine Implementierung", fehler.Message, StringComparison.Ordinal);
    }
}
