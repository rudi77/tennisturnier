using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Domain.Tests.Scheduling;

/// <summary>
/// Die Dauerschätzung. Sie ist eine Schätzung und nichts weiter — geprüft wird
/// deshalb nicht eine Zahl auf die Minute, sondern dass die Verhältnisse
/// stimmen: was länger dauert, bekommt mehr Zeit.
/// </summary>
public sealed class MatchDurationTests
{
    private static readonly MatchFormat ShortSets = new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6);
    private static readonly MatchFormat FullSets = new(BestOf: 3, FinalSetMode.Advantage, TiebreakAt: 6);
    private static readonly MatchFormat Long = new(BestOf: 5, FinalSetMode.Regular, TiebreakAt: 6);

    [Fact]
    public void Der_Match_Tiebreak_verkuerzt_gegenueber_einem_ausgespielten_Entscheidungssatz()
    {
        // Nur eine Größe unterscheidet die beiden: der Entscheidungssatz. Der
        // Vergleich mit „Advantage" änderte zwei auf einmal und bewiese nichts
        // über den Tiebreak.
        var regular = new MatchFormat(BestOf: 3, FinalSetMode.Regular, TiebreakAt: 6);

        Assert.True(MatchDuration.Estimate(ShortSets) < MatchDuration.Estimate(regular));
    }

    [Fact]
    public void Ein_ausgespielter_Entscheidungssatz_dauert_laenger_als_ein_regulaerer()
    {
        var regular = new MatchFormat(BestOf: 3, FinalSetMode.Regular, TiebreakAt: 6);

        Assert.True(MatchDuration.Estimate(regular) < MatchDuration.Estimate(FullSets));
    }

    [Fact]
    public void Drei_Gewinnsaetze_dauern_laenger_als_zwei()
    {
        Assert.True(MatchDuration.Estimate(FullSets) < MatchDuration.Estimate(Long));
    }

    [Fact]
    public void Ein_Finale_bekommt_mehr_Zeit()
    {
        Assert.True(MatchDuration.Estimate(ShortSets, isFinal: true) > MatchDuration.Estimate(ShortSets));
    }

    [Theory]
    [InlineData(3, FinalSetMode.MatchTiebreak10)]
    [InlineData(3, FinalSetMode.Advantage)]
    [InlineData(5, FinalSetMode.Regular)]
    [InlineData(1, FinalSetMode.Regular)]
    public void Jede_Schaetzung_ist_ein_brauchbares_Zeitfenster(int bestOf, FinalSetMode mode)
    {
        var estimate = MatchDuration.Estimate(new MatchFormat(bestOf, mode, TiebreakAt: 6));

        // Eine Schätzung auf die Minute wäre eine Behauptung; gerundet wird auf
        // fünf Minuten.
        Assert.Equal(0, estimate.TotalMinutes % 5);
        Assert.InRange(estimate, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(240));
    }
}
