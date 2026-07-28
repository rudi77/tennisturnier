namespace TennisTurnier.Domain.Common;

/// <summary>
/// Halboffenes Zeitintervall <c>[Start, End)</c> auf der absoluten Zeitachse.
///
/// Halboffen ist hier keine Feinheit: zwei direkt aneinander grenzende Matches
/// (10:00–11:30 und 11:30–13:00) dürfen nicht als Überschneidung gelten,
/// derselbe Platz zur selben Minute aber sehr wohl.
/// </summary>
public readonly record struct TimeSlot
{
    public TimeSlot(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new DomainException($"Ein Zeitfenster muss vorwärts laufen, war aber {start:O} bis {end:O}.");
        }

        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    public TimeSpan Duration => End - Start;

    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < End;

    public bool Overlaps(TimeSlot other) => Start < other.End && other.Start < End;

    public TimeSlot? Intersect(TimeSlot other)
    {
        var start = Start > other.Start ? Start : other.Start;
        var end = End < other.End ? End : other.End;

        return end > start ? new TimeSlot(start, end) : null;
    }

    /// <summary>
    /// Zieht <paramref name="other"/> ab. Ergibt kein, ein oder — bei einer Sperre
    /// mitten im Fenster — zwei Reststücke.
    /// </summary>
    public IEnumerable<TimeSlot> Subtract(TimeSlot other)
    {
        if (!Overlaps(other))
        {
            yield return this;
            yield break;
        }

        if (Start < other.Start)
        {
            yield return new TimeSlot(Start, other.Start);
        }

        if (other.End < End)
        {
            yield return new TimeSlot(other.End, End);
        }
    }

    /// <summary>
    /// Zieht alle <paramref name="subtrahends"/> von allen <paramref name="slots"/> ab.
    /// Das Ergebnis ist nach Startzeit sortiert und überschneidungsfrei, sofern es
    /// die Eingabe war.
    /// </summary>
    public static IReadOnlyList<TimeSlot> SubtractAll(
        IEnumerable<TimeSlot> slots,
        IEnumerable<TimeSlot> subtrahends)
    {
        var remaining = slots.ToList();

        foreach (var subtrahend in subtrahends)
        {
            remaining = remaining.SelectMany(slot => slot.Subtract(subtrahend)).ToList();
        }

        return remaining.OrderBy(slot => slot.Start).ToList();
    }

    /// <summary>
    /// Führt aneinander grenzende oder überlappende Fenster zusammen. Ohne diesen
    /// Schritt zerfällt ein durchgehender Öffnungszeitraum, der über Mitternacht
    /// oder über zwei Regeln hinweg definiert ist, in scheinbar getrennte Fenster.
    /// </summary>
    public static IReadOnlyList<TimeSlot> Merge(IEnumerable<TimeSlot> slots)
    {
        var sorted = slots.OrderBy(slot => slot.Start).ToList();
        var merged = new List<TimeSlot>(sorted.Count);

        foreach (var slot in sorted)
        {
            if (merged.Count > 0 && slot.Start <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = new TimeSlot(last.Start, last.End > slot.End ? last.End : slot.End);
            }
            else
            {
                merged.Add(slot);
            }
        }

        return merged;
    }

    public override string ToString() => $"{Start:yyyy-MM-dd HH:mm}–{End:HH:mm}";
}
