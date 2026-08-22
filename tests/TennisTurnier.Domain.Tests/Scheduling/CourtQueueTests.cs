using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Domain.Tests.Scheduling;

/// <summary>
/// Die Warteschlange eines Platzes. Der Kern ist, dass die Reihenfolge die
/// Aussage ist und die Zeiten dahinter nachgezogen werden — beim Tennis ist die
/// Matchdauer unbekannt, und ein starres Raster kippt beim ersten langen Match
/// (ADR-0002).
/// </summary>
public sealed class CourtQueueTests
{
    private readonly Guid _tournamentId = Guid.NewGuid();
    private readonly Guid _courtId = Guid.NewGuid();

    private static DateTimeOffset At(int hour, int minute = 0) =>
        new(2026, 5, 16, hour, minute, 0, TimeSpan.Zero);

    private CourtAssignment Planned(int sequence, DateTimeOffset start, int minutes = 75)
    {
        var assignment = new CourtAssignment(
            Guid.NewGuid(), _tournamentId, Guid.NewGuid(), _courtId, sequence,
            TimeSpan.FromMinutes(minutes), AssignmentSource.Auto);

        assignment.PlanFor(start);

        return assignment;
    }

    [Fact]
    public void Ein_laenger_dauerndes_Match_schiebt_die_Warteschlange()
    {
        // Der Normalfall am Turniertag: das erste Match beginnt zwanzig Minuten
        // später als geplant, und alles dahinter rutscht mit. Ein Aushang, der
        // das verschweigt, ist unbrauchbar.
        var running = Planned(1, At(9));
        running.Start(At(9, 20));

        var queue = new[] { running, Planned(2, At(10, 25)), Planned(3, At(11, 50)) };

        // Begonnen um 9:20, geschätzte Dauer 75 Minuten, dazu die Wechselpause.
        var changed = CourtQueue.Reflow(queue, CourtQueue.FreeFrom(queue, At(10)), At(10));

        Assert.Equal(2, changed);
        Assert.Equal(At(10, 45), queue[1].PlannedStart);
        Assert.Equal(At(12, 10), queue[2].PlannedStart);
    }

    [Fact]
    public void Ein_planmaessiger_Ablauf_aendert_nichts()
    {
        // Die Gegenprobe: läuft alles wie geschätzt, bleibt der Aushang stehen.
        // Ein Nachziehen, das immer etwas ändert, wäre keins.
        var running = Planned(1, At(9));
        running.Start(At(9));

        var queue = new[] { running, Planned(2, At(10, 25)), Planned(3, At(11, 50)) };

        Assert.Equal(0, CourtQueue.Reflow(queue, CourtQueue.FreeFrom(queue, At(10)), At(10)));
    }

    [Fact]
    public void Ein_ueberzogenes_Match_verschiebt_alles_Wartende_nach_hinten()
    {
        var running = Planned(1, At(9));
        running.Start(At(9));

        var queue = new[] { running, Planned(2, At(10, 25)), Planned(3, At(11, 50)) };

        // Es ist schon 11 Uhr und das Match läuft noch: der Platz wird erst
        // frei, wenn es endet.
        CourtQueue.Reflow(queue, CourtQueue.FreeFrom(queue, At(11)), At(11));

        Assert.Equal(At(11, 10), queue[1].PlannedStart);
        Assert.Equal(At(12, 35), queue[2].PlannedStart);
    }

    [Fact]
    public void Ein_frueher_beendetes_Match_zieht_die_Warteschlange_vor()
    {
        var finished = Planned(1, At(9));
        finished.Start(At(9));
        finished.Finish(At(9, 50));

        var queue = new[] { finished, Planned(2, At(10, 25)), Planned(3, At(11, 50)) };

        CourtQueue.Reflow(queue, CourtQueue.FreeFrom(queue, At(9, 50)), At(9, 50));

        Assert.Equal(At(9, 50), queue[1].PlannedStart);
        Assert.Equal(At(11, 15), queue[2].PlannedStart);
    }

    [Fact]
    public void Eine_Zusage_wird_nie_unterlaufen()
    {
        // „Nicht vor 14 Uhr" ist das Einzige, worauf sich ein Spieler verlassen
        // kann. Ihn früher aufzurufen wäre genau der Vertrauensbruch, den der
        // Turniertagbetrieb vermeiden soll.
        var waiting = Planned(1, At(10));
        waiting.PromiseNotBefore(At(14));

        CourtQueue.Reflow([waiting], At(9), At(9));

        Assert.Equal(At(14), waiting.PlannedStart);
    }

    [Fact]
    public void Ohne_Belegung_beginnt_die_Warteschlange_jetzt()
    {
        var queue = new[] { Planned(1, At(8)), Planned(2, At(9, 25)) };

        CourtQueue.Reflow(queue, CourtQueue.FreeFrom(queue, At(11)), At(11));

        Assert.Equal(At(11), queue[0].PlannedStart);
        Assert.Equal(At(12, 25), queue[1].PlannedStart);
    }

    [Fact]
    public void Eine_unterbrochene_Partie_wartet_nicht_auf_ihren_Platz()
    {
        // Sie wartet auf ihre Fortsetzung, und die kann anderswo stattfinden.
        // Sie im Platzbelegungsplan stehen zu lassen hieße, seine Zeit für etwas
        // zu blockieren, das dort vielleicht nie wieder stattfindet.
        var suspended = Planned(1, At(9));
        suspended.Start(At(9));
        suspended.Suspend();

        var queue = new[] { suspended, Planned(2, At(10, 25)) };

        Assert.Equal([queue[1]], CourtQueue.Waiting(queue));

        CourtQueue.Reflow(queue, CourtQueue.FreeFrom(queue, At(10)), At(10));

        Assert.Equal(At(10), queue[1].PlannedStart);
    }

    [Fact]
    public void Die_Reihenfolge_laesst_sich_umstellen_und_wird_lueckenlos_nummeriert()
    {
        var first = Planned(1, At(9));
        var second = Planned(2, At(10, 25));
        var third = Planned(3, At(11, 50));

        CourtQueue.Reorder([first, second, third], [third.Id, first.Id, second.Id]);

        Assert.Equal(1, third.SequenceOnCourt);
        Assert.Equal(2, first.SequenceOnCourt);
        Assert.Equal(3, second.SequenceOnCourt);
        Assert.Equal([third, first, second], CourtQueue.Waiting([first, second, third]));
    }

    [Fact]
    public void Eine_unvollstaendige_Reihenfolge_wird_abgewiesen()
    {
        // Sonst bliebe eine Zuweisung mit ihrer alten Nummer stehen, und zwei
        // Matches trügen dieselbe Position — am Platz wird die Nummer vorgelesen.
        var first = Planned(1, At(9));
        var second = Planned(2, At(10, 25));

        Assert.Throws<DomainException>(() => CourtQueue.Reorder([first, second], [first.Id]));
        Assert.Throws<DomainException>(() => CourtQueue.Reorder([first, second], [first.Id, first.Id]));
        Assert.Throws<DomainException>(
            () => CourtQueue.Reorder([first, second], [first.Id, second.Id, Guid.NewGuid()]));
    }

    [Fact]
    public void Beendete_Zuweisungen_zaehlen_nicht_mehr_zur_Warteschlange()
    {
        var finished = Planned(1, At(9));
        finished.Start(At(9));
        finished.Finish(At(10));

        Assert.Empty(CourtQueue.Waiting([finished]));
    }

    [Fact]
    public void Ein_zweiter_Durchlauf_ohne_Aenderung_zaehlt_nichts()
    {
        // Die Warteschlange wird nach jedem Zug neu gerechnet. Bliebe sie dabei
        // „geändert", schriebe die Anwendung bei jedem Aufruf dieselben Zeiten
        // erneut — und jeder Zuschauer bekäme einen Push ohne Neuigkeit.
        var queue = new[] { Planned(1, At(9)), Planned(2, At(10, 20)) };

        CourtQueue.Reflow(queue, At(9), At(9));

        Assert.Equal(0, CourtQueue.Reflow(queue, At(9), At(9)));
    }

    [Fact]
    public void Ohne_geplante_Zeit_steht_eine_Zuweisung_hinten()
    {
        // Zwei Zuweisungen mit derselben Nummer — von Hand entstanden. Die ohne
        // Zeit kommt zuletzt: eine Reihenfolge muss auch dann eine sein.
        var ohneZeit = new CourtAssignment(
            Guid.NewGuid(), _tournamentId, Guid.NewGuid(), _courtId, 1,
            TimeSpan.FromMinutes(75), AssignmentSource.Manual);

        var mitZeit = Planned(1, At(9));

        var wartend = CourtQueue.Waiting([ohneZeit, mitZeit]);

        Assert.Equal([mitZeit.Id, ohneZeit.Id], wartend.Select(a => a.Id));
    }

    [Fact]
    public void Eine_Zuweisung_ohne_Zeit_bekommt_eine()
    {
        // Von Hand auf den Platz gesetzt, ohne Zeitangabe: die Warteschlange
        // trägt sie nach, sonst stünde sie ohne Beginn im Aushang.
        var ohneZeit = new CourtAssignment(
            Guid.NewGuid(), _tournamentId, Guid.NewGuid(), _courtId, 1,
            TimeSpan.FromMinutes(75), AssignmentSource.Manual);

        var geaendert = CourtQueue.Reflow([ohneZeit], At(9), At(9));

        Assert.Equal(1, geaendert);
        Assert.Equal(At(9), ohneZeit.PlannedStart);
    }
}
