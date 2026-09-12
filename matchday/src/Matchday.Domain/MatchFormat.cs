namespace Matchday.Domain;

/// <summary>Wie der letzte Satz gespielt wird.</summary>
public enum FinalSetMode
{
    /// <summary>Wie jeder andere Satz.</summary>
    Regular,

    /// <summary>Match-Tiebreak bis 10 statt eines dritten Satzes.</summary>
    MatchTiebreak10,

    /// <summary>Durchspielen mit zwei Spielen Vorsprung, ohne Tiebreak.</summary>
    Advantage,
}

/// <summary>
/// Wie ein Match gespielt wird. Gilt für das ganze Turnier (ADR-0011, vereinfacht).
/// Übernommen aus dem alten Baum.
/// </summary>
public sealed record MatchFormat(
    int BestOf = 3,
    FinalSetMode FinalSetMode = FinalSetMode.MatchTiebreak10,
    int TiebreakAt = 6)
{
    public static MatchFormat Standard { get; } = new();

    public void Validate()
    {
        if (BestOf is not (1 or 3 or 5))
        {
            throw new DomainException($"Ein Match geht über 1, 3 oder 5 Sätze, nicht über {BestOf}.");
        }

        if (TiebreakAt is < 1 or > 12)
        {
            throw new DomainException($"Ein Satz geht bis 1 bis 12 Spiele, nicht bis {TiebreakAt}.");
        }
    }

    /// <summary>Sätze, die zum Sieg genügen.</summary>
    public int SetsToWin => (BestOf / 2) + 1;

    public string Describe()
    {
        var sets = BestOf == 1 ? "ein Satz" : $"{SetsToWin} Gewinnsätze";
        var length = TiebreakAt == 6 ? "" : $" bis {TiebreakAt}";
        var final = FinalSetMode switch
        {
            FinalSetMode.MatchTiebreak10 when BestOf > 1 => ", Match-Tiebreak statt des letzten Satzes",
            FinalSetMode.Advantage => ", letzter Satz ohne Tiebreak",
            _ => "",
        };

        return sets + length + final;
    }
}
