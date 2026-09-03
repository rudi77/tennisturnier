using TennisTurnier.Application.Common;
using TennisTurnier.Application.Tests.Fakes;

namespace TennisTurnier.Application.Tests;

/// <summary>
/// Die Warteschlange für alles, was erst nach einem erfolgreichen Speichern
/// passieren darf.
///
/// Der Anlass ist der Push an die Zuschauer: ginge er vorher hinaus, holten sich
/// alle einen Stand, den es nach einem Rollback nie gegeben hat.
/// </summary>
public sealed class PostCommitQueueTests
{
    [Fact]
    public async Task Nichts_laeuft_bevor_abgeraeumt_wird()
    {
        var queue = new PostCommitQueue(new SammelndeFehlermeldung());
        var ran = false;

        queue.Enqueue(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.False(ran);

        await queue.DrainAsync();

        Assert.True(ran);
    }

    [Fact]
    public async Task Die_Reihenfolge_bleibt_erhalten()
    {
        var queue = new PostCommitQueue(new SammelndeFehlermeldung());
        var order = new List<int>();

        for (var index = 1; index <= 3; index++)
        {
            var captured = index;
            queue.Enqueue(_ =>
            {
                order.Add(captured);
                return Task.CompletedTask;
            });
        }

        await queue.DrainAsync();

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public async Task Zweimal_abraeumen_verschickt_nicht_zweimal()
    {
        // Ein Anwendungsfall kann in zwei Schritten speichern. Ohne diese
        // Eigenschaft bekäme jeder Zuschauer dieselbe Meldung doppelt.
        var queue = new PostCommitQueue(new SammelndeFehlermeldung());
        var runs = 0;

        queue.Enqueue(_ =>
        {
            runs++;
            return Task.CompletedTask;
        });

        await queue.DrainAsync();
        await queue.DrainAsync();

        Assert.Equal(1, runs);
    }

    /// <summary>
    /// Ein gescheiterter Nachlauf kippt den Schreibvorgang nicht.
    ///
    /// Was hier läuft, ist nicht mehr Teil des Speicherns: der Commit ist
    /// durch, das Ergebnis steht in der Datenbank. Eine durchgereichte Ausnahme
    /// machte daraus eine 500 auf einen Aufruf, der gelungen ist — und der
    /// Aufrufer bekäme beim zweiten Versuch einen Konflikt.
    /// </summary>
    [Fact]
    public async Task Ein_gescheiterter_Nachlauf_wird_gemeldet_und_nicht_geworfen()
    {
        var fehler = new SammelndeFehlermeldung();
        var queue = new PostCommitQueue(fehler);
        var danach = false;

        queue.Enqueue(_ => throw new InvalidOperationException("Der Hub ist weg."));
        queue.Enqueue(_ =>
        {
            danach = true;
            return Task.CompletedTask;
        });

        await queue.DrainAsync();

        // Und die übrigen Handlungen laufen weiter: der Hinweis an den Feed
        // fiele sonst mit dem Push zusammen aus.
        Assert.True(danach);

        var gemeldet = Assert.Single(fehler.Gemeldet);
        Assert.Equal("Der Hub ist weg.", gemeldet.Message);
    }
}
