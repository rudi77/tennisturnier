using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Scheduling;

/// <summary>Ein Platz, wie ihn der Spielplan sieht: Name, Rang und freie Fenster.</summary>
public sealed record SchedulableCourt(
    Guid Id,
    string Name,
    bool IsCenterCourt,
    IReadOnlyList<TimeSlot> FreeWindows);

/// <summary>
/// Die Aufgabe, die der Solver lösen soll.
///
/// Sie enthält bewusst keine Wünsche und keine Gewichte, sondern nur Tatsachen:
/// welche Matches es gibt, wer darin spielt, wann welcher Platz frei ist, wie
/// lange ein Match schätzungsweise dauert und was bereits von Hand gesetzt
/// wurde. Was daraus ein guter Plan ist, entscheidet der Solver — und dass er
/// zulässig ist, entscheidet der <see cref="ScheduleValidator"/>, nicht er
/// selbst (ADR-0002).
/// </summary>
public sealed record SchedulingProblem(
    IReadOnlyList<Match> Matches,
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> PlayersByEntry,
    IReadOnlyList<SchedulableCourt> Courts,
    IReadOnlyDictionary<Guid, TimeSpan> DurationByMatch,
    TimeSpan MinimumRest,
    IReadOnlyList<CourtAssignment> Existing)
{
    /// <summary>Der Prüfstand für einen Vorschlag — dieselben Daten, andere Sicht.</summary>
    public SchedulingContext ToContext() => new(
        Matches,
        PlayersByEntry,
        Courts.ToDictionary(court => court.Id, court => court.FreeWindows),
        MinimumRest);

    public TimeSpan DurationOf(Guid matchId)
    {
        if (DurationByMatch.TryGetValue(matchId, out var duration))
        {
            return duration;
        }

        return MatchDuration.Default;
    }
}

/// <summary>
/// Wie lange ein Match voraussichtlich dauert.
///
/// Eine Schätzung und nichts weiter — die tatsächliche Dauer ist unbekannt, und
/// genau darum gibt es am Turniertag Warteschlangen statt eines Zeitrasters
/// (ADR-0002). Für die Machbarkeitsprüfung im Planungsmodus braucht es dennoch
/// eine Zahl, und eine begründete ist besser als eine geratene.
/// </summary>
public static class MatchDuration
{
    public static readonly TimeSpan Default = TimeSpan.FromMinutes(90);

    /// <summary>Ein Satz bis sechs — das Maß, an dem die Schätzung hängt.</summary>
    private const int RegularSetTarget = 6;

    /// <summary>
    /// Aus dem Satzformat und der Bedeutung des Matches.
    ///
    /// Zwei Gewinnsätze mit Match-Tiebreak sind in gut einer Stunde vorbei, drei
    /// volle Sätze dauern deutlich länger, und ein Finale zieht sich — dort wird
    /// um jeden Punkt gespielt.
    ///
    /// Die Zahl der Spiele je Satz geht mit ein und ist kein Beiwerk: Sätze bis
    /// vier werden gespielt, weil der Nachmittag nicht länger ist, und ein
    /// Plan, der sie wie Sätze bis sechs veranschlagt, gibt genau die Zeit
    /// wieder aus, die damit gewonnen werden sollte.
    /// </summary>
    public static TimeSpan Estimate(MatchFormat format, bool isFinal = false)
    {
        ArgumentNullException.ThrowIfNull(format);

        var perSet = format.FinalSetMode == FinalSetMode.Advantage
            ? TimeSpan.FromMinutes(45)
            : TimeSpan.FromMinutes(40);

        perSet *= (double)format.TiebreakAt / RegularSetTarget;

        // Gerechnet wird mit der Zahl der Sätze, die zum Sieg nötig sind, plus
        // einem halben weiteren: die wenigsten Matches gehen über die volle
        // Distanz, aber genug davon, dass ein knapper Plan sonst reihum kippt.
        var sets = format.SetsToWin + 0.5;

        // Der Entscheidungssatz als Match-Tiebreak ist kürzer als ein Satz.
        if (format.FinalSetMode == FinalSetMode.MatchTiebreak10)
        {
            sets -= 0.3;
        }

        var estimate = perSet * sets;

        return Round(isFinal ? estimate + TimeSpan.FromMinutes(15) : estimate);
    }

    /// <summary>Auf fünf Minuten gerundet — eine Schätzung auf die Minute genau wäre eine Behauptung.</summary>
    private static TimeSpan Round(TimeSpan value) =>
        TimeSpan.FromMinutes(Math.Round(value.TotalMinutes / 5) * 5);
}
