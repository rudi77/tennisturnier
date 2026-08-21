namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die Systemuhr der Composition Root.
///
/// Eine Zeile, und trotzdem geprüft: sie liest UTC. Läse sie die Ortszeit,
/// verschöbe sich jede gespeicherte Zeit um den Zonenversatz des Servers —
/// und zwar still, weil in der Entwicklung meist beides dasselbe ist.
/// </summary>
public sealed class SystemClockTests
{
    [Fact]
    public void Liest_die_Uhr_in_UTC()
    {
        var vorher = DateTimeOffset.UtcNow;
        var jetzt = new SystemClock().Now;
        var nachher = DateTimeOffset.UtcNow;

        Assert.InRange(jetzt, vorher, nachher);
        Assert.Equal(TimeSpan.Zero, jetzt.Offset);
    }
}
