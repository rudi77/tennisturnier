using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Domain.Tests.Scheduling;

/// <summary>
/// Die Zuweisung eines Matches auf einen Platz — und ihr Zustandsautomat.
///
/// Der Automat ist der Grund, aus dem der Turniertag überhaupt funktioniert:
/// aufgerufen wird nur, was eingeplant oder unterbrochen ist; gestartet nur,
/// was aufgerufen wurde. Wer diese Schranken aufweicht, bekommt einen Platz,
/// auf dem zwei Matches gleichzeitig stehen.
/// </summary>
public sealed class CourtAssignmentTests
{
    private static readonly DateTimeOffset Vormittag = new(2026, 5, 16, 9, 0, 0, TimeSpan.FromHours(2));

    private static CourtAssignment Bauen(
        TimeSpan? dauer = null,
        int position = 1,
        AssignmentSource quelle = AssignmentSource.Auto) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            position,
            dauer ?? TimeSpan.FromMinutes(75),
            quelle);

    [Fact]
    public void Braucht_Turnier_Match_und_Platz()
    {
        foreach (var (turnier, match, platz) in new[]
        {
            (Guid.Empty, Guid.NewGuid(), Guid.NewGuid()),
            (Guid.NewGuid(), Guid.Empty, Guid.NewGuid()),
            (Guid.NewGuid(), Guid.NewGuid(), Guid.Empty),
        })
        {
            var fehler = Assert.Throws<DomainException>(() =>
                new CourtAssignment(
                    Guid.NewGuid(), turnier, match, platz, 1, TimeSpan.FromHours(1), AssignmentSource.Auto));

            Assert.Contains("Turnier, Match und Platz", fehler.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void Die_geschaetzte_Dauer_muss_positiv_sein(int minuten)
    {
        var fehler = Assert.Throws<DomainException>(() => Bauen(TimeSpan.FromMinutes(minuten)));

        Assert.Contains("muss positiv sein", fehler.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Eine_Position_in_der_Warteschlange_beginnt_bei_eins(int position)
    {
        var fehler = Assert.Throws<DomainException>(() => Bauen(position: position));

        Assert.Contains($"war {position}", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Planzeit_gibt_es_kein_Zeitfenster()
    {
        var zuweisung = Bauen();
        Assert.Null(zuweisung.PlannedSlot);

        zuweisung.PlanFor(Vormittag);

        Assert.Equal(new TimeSlot(Vormittag, Vormittag + TimeSpan.FromMinutes(75)), zuweisung.PlannedSlot);
    }

    [Fact]
    public void Eine_Umplanung_darf_die_Dauer_mitbringen_oder_lassen()
    {
        var zuweisung = Bauen();

        zuweisung.PlanFor(Vormittag, TimeSpan.FromMinutes(90));
        Assert.Equal(TimeSpan.FromMinutes(90), zuweisung.EstimatedDuration);

        zuweisung.PlanFor(Vormittag.AddHours(1));
        Assert.Equal(TimeSpan.FromMinutes(90), zuweisung.EstimatedDuration);
    }

    [Fact]
    public void Eine_Planung_mit_unmoeglicher_Dauer_wird_abgewiesen()
    {
        var zuweisung = Bauen();

        var fehler = Assert.Throws<DomainException>(() => zuweisung.PlanFor(Vormittag, TimeSpan.Zero));

        Assert.Contains("muss positiv sein", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Zusage_steht_fuer_sich()
    {
        var zuweisung = Bauen();

        zuweisung.PromiseNotBefore(Vormittag);

        Assert.Equal(Vormittag, zuweisung.EarliestStart);
        Assert.Null(zuweisung.PlannedStart);
    }

    [Fact]
    public void Der_Solver_darf_Gesetztes_und_Festgenageltes_nicht_anruehren()
    {
        var zuweisung = Bauen();
        Assert.False(zuweisung.IsFixedForSolver);

        zuweisung.MarkAs(AssignmentSource.Manual);
        Assert.True(zuweisung.IsFixedForSolver);

        zuweisung.MarkAs(AssignmentSource.Pinned);
        Assert.True(zuweisung.IsFixedForSolver);
    }

    [Fact]
    public void Eine_Umplanung_setzt_alles_auf_einmal()
    {
        var zuweisung = Bauen();
        zuweisung.PlanFor(Vormittag);
        var platz = Guid.NewGuid();

        zuweisung.Replan(
            platz,
            sequenceOnCourt: 3,
            plannedStart: null,
            earliestStart: Vormittag.AddHours(5),
            estimatedDuration: TimeSpan.FromMinutes(45),
            source: AssignmentSource.Pinned);

        Assert.Equal(platz, zuweisung.CourtId);
        Assert.Equal(3, zuweisung.SequenceOnCourt);
        // Eine Umplanung ohne Planzeit lässt keine alte stehen.
        Assert.Null(zuweisung.PlannedStart);
        Assert.Equal(Vormittag.AddHours(5), zuweisung.EarliestStart);
        Assert.Equal(TimeSpan.FromMinutes(45), zuweisung.EstimatedDuration);
        Assert.Equal(AssignmentSource.Pinned, zuweisung.Source);
    }

    [Fact]
    public void Eine_Umplanung_braucht_einen_Platz_und_eine_moegliche_Dauer()
    {
        var zuweisung = Bauen();

        var ohnePlatz = Assert.Throws<DomainException>(() =>
            zuweisung.Replan(Guid.Empty, 1, null, null, TimeSpan.FromHours(1), AssignmentSource.Auto));
        Assert.Contains("braucht einen Platz", ohnePlatz.Message, StringComparison.Ordinal);

        var ohneDauer = Assert.Throws<DomainException>(() =>
            zuweisung.Replan(Guid.NewGuid(), 1, null, null, TimeSpan.Zero, AssignmentSource.Auto));
        Assert.Contains("muss positiv sein", ohneDauer.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_aufgerufene_Zuweisung_laesst_sich_nicht_mehr_umplanen()
    {
        var zuweisung = Bauen();
        zuweisung.Call();

        var fehler = Assert.Throws<DomainException>(() =>
            zuweisung.Replan(Guid.NewGuid(), 1, null, null, TimeSpan.FromHours(1), AssignmentSource.Auto));

        Assert.Contains("Zustand [Planned]", fehler.Message, StringComparison.Ordinal);
        Assert.Contains("war Called", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Der_Weg_ueber_den_Tag_fuehrt_von_Aufruf_ueber_Start_zum_Ende()
    {
        var zuweisung = Bauen();

        zuweisung.Call();
        Assert.Equal(AssignmentStatus.Called, zuweisung.Status);

        zuweisung.Start(Vormittag);
        Assert.Equal(AssignmentStatus.Running, zuweisung.Status);
        Assert.Equal(Vormittag, zuweisung.ActualStart);

        zuweisung.Finish(Vormittag.AddHours(1));
        Assert.Equal(AssignmentStatus.Finished, zuweisung.Status);
        Assert.True(zuweisung.IsOver);
    }

    [Fact]
    public void Eine_Fortsetzung_verschiebt_den_Beginn_nicht()
    {
        // Nach einer Regenpause fängt das Match nicht neu an — es geht weiter.
        // Der Beginn bleibt der ursprüngliche, sonst wäre die gespielte Dauer
        // um die Pause zu kurz.
        var zuweisung = Bauen();
        zuweisung.Call();
        zuweisung.Start(Vormittag);
        zuweisung.Suspend();

        zuweisung.Start(Vormittag.AddHours(2));

        Assert.Equal(Vormittag, zuweisung.ActualStart);
        Assert.Equal(AssignmentStatus.Running, zuweisung.Status);
    }

    [Fact]
    public void Ein_eingeplantes_Match_laesst_sich_ohne_Aufruf_starten()
    {
        var zuweisung = Bauen();

        zuweisung.Start(Vormittag);

        Assert.Equal(AssignmentStatus.Running, zuweisung.Status);
    }

    [Fact]
    public void Ein_laufendes_Match_laesst_sich_unterbrechen_und_fortsetzen()
    {
        var zuweisung = Bauen();
        zuweisung.Call();
        zuweisung.Start(Vormittag);

        zuweisung.Suspend();
        Assert.Equal(AssignmentStatus.Suspended, zuweisung.Status);

        // Aus der Unterbrechung heraus geht beides: erneut aufrufen oder direkt
        // weiterspielen.
        zuweisung.Call();
        Assert.Equal(AssignmentStatus.Called, zuweisung.Status);
    }

    [Fact]
    public void Ein_aufgerufenes_Match_gibt_den_Platz_frei_ohne_je_begonnen_zu_haben()
    {
        // Der Normalfall des Nichtantretens.
        var zuweisung = Bauen();
        zuweisung.Call();

        zuweisung.Finish(Vormittag);

        Assert.Equal(AssignmentStatus.Finished, zuweisung.Status);
        Assert.Null(zuweisung.ActualStart);
    }

    [Fact]
    public void Das_Ende_liegt_nicht_vor_dem_Beginn()
    {
        var zuweisung = Bauen();
        zuweisung.Call();
        zuweisung.Start(Vormittag);

        var fehler = Assert.Throws<DomainException>(() => zuweisung.Finish(Vormittag.AddHours(-1)));

        Assert.Contains("liegt vor dem Beginn", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_eingeplantes_Match_laesst_sich_nicht_beenden()
    {
        var zuweisung = Bauen();

        var fehler = Assert.Throws<DomainException>(() => zuweisung.Finish(Vormittag));

        Assert.Contains("war Planned", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_beendetes_Match_laesst_sich_nicht_mehr_unterbrechen()
    {
        var zuweisung = Bauen();
        zuweisung.Call();
        zuweisung.Start(Vormittag);
        zuweisung.Finish(Vormittag.AddHours(1));

        Assert.Throws<DomainException>(() => zuweisung.Suspend());
    }

    [Fact]
    public void Jede_Aenderung_zaehlt_die_Version_hoch()
    {
        var zuweisung = Bauen();
        var anfang = zuweisung.Version;

        zuweisung.SetSequence(2);
        zuweisung.PromiseNotBefore(Vormittag);
        zuweisung.MarkAs(AssignmentSource.Manual);

        Assert.Equal(anfang + 3, zuweisung.Version);
    }

    [Fact]
    public void Nennt_sich_mit_Match_Platz_Position_und_Zustand()
    {
        var zuweisung = Bauen(position: 2);

        Assert.Contains($"Match {zuweisung.MatchId}", zuweisung.ToString(), StringComparison.Ordinal);
        Assert.Contains("Position 2", zuweisung.ToString(), StringComparison.Ordinal);
        Assert.Contains("(Planned)", zuweisung.ToString(), StringComparison.Ordinal);
    }
}
