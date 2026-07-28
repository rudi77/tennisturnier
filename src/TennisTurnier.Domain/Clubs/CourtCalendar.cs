using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Clubs;

/// <summary>
/// Rechnet die Öffnungszeiten eines Platzes abzüglich seiner Sperren in konkrete
/// freie Fenster auf der absoluten Zeitachse um.
///
/// Das ist reine Domänenlogik ohne Infrastruktur und später die <em>einzige</em>
/// Quelle der Platzverfügbarkeit — sowohl für die manuelle Zuweisung als auch für
/// den Solver aus ADR-0002. Zwei Quellen für dieselbe Frage wären der sichere Weg
/// zu einem Spielplan, der auf einem gesperrten Platz landet.
/// </summary>
public sealed class CourtCalendar
{
    private readonly TimeZoneInfo _clubTimeZone;

    public CourtCalendar(TimeZoneInfo clubTimeZone) =>
        _clubTimeZone = clubTimeZone ?? throw new ArgumentNullException(nameof(clubTimeZone));

    /// <summary>
    /// Liefert die freien Fenster des Platzes innerhalb von <paramref name="range"/>,
    /// sortiert und überschneidungsfrei. Ein stillgelegter Platz liefert nichts.
    /// </summary>
    /// <summary>
    /// Der Zeitraum eines Turniers in der Zeitzone des Vereins, mit einem Tag
    /// Luft nach jeder Seite.
    /// </summary>
    ///
    /// <remarks>
    /// Die Luft ist kein Schlendrian: ein Abendspiel kann über Mitternacht
    /// gehen, und ein Verein westlich von UTC beginnt seinen ersten Turniertag
    /// nach UTC-Mitternacht. Vor allem aber steht die Rechnung damit an genau
    /// einer Stelle — sonst hielte die Spielplanprüfung eine Ansetzung für
    /// zulässig, die der Solver nie vorgeschlagen hätte, oder umgekehrt.
    /// </remarks>
    public TimeSlot TournamentRange(DateOnly startsOn, DateOnly endsOn) =>
        new(LocalMidnight(startsOn.AddDays(-1)), LocalMidnight(endsOn.AddDays(2)));

    private DateTimeOffset LocalMidnight(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);

        return new DateTimeOffset(local, _clubTimeZone.GetUtcOffset(local));
    }

    public IReadOnlyList<TimeSlot> FreeWindows(Court court, TimeSlot range)
    {
        ArgumentNullException.ThrowIfNull(court);

        if (!court.IsActive)
        {
            return [];
        }

        var open = TimeSlot.Merge(OpeningHours(court, range));
        var blocked = court.Blocks.Select(block => block.Period);

        return TimeSlot.SubtractAll(open, blocked);
    }

    /// <summary>
    /// Öffnungszeiten ohne Berücksichtigung der Sperren, auf den Bereich zugeschnitten.
    /// </summary>
    private IEnumerable<TimeSlot> OpeningHours(Court court, TimeSlot range)
    {
        foreach (var date in LocalDatesTouching(range))
        {
            foreach (var window in court.Availability.Where(w => w.AppliesOn(date)))
            {
                var opens = Resolve(date, window.OpensAt, Ambiguity.Earliest);
                var closes = Resolve(date, window.ClosesAt, Ambiguity.Latest);

                if (closes <= opens)
                {
                    // Kann nur entstehen, wenn eine Zeitumstellung das Fenster
                    // vollständig verschluckt. Dann gibt es an diesem Tag nichts.
                    continue;
                }

                if (new TimeSlot(opens, closes).Intersect(range) is { } clipped)
                {
                    yield return clipped;
                }
            }
        }
    }

    /// <summary>
    /// Alle lokalen Kalendertage, die den Bereich berühren können. Der Puffer von
    /// einem Tag an beiden Enden deckt die Verschiebung zwischen UTC und lokaler
    /// Zeit ab.
    /// </summary>
    private IEnumerable<DateOnly> LocalDatesTouching(TimeSlot range)
    {
        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(range.Start, _clubTimeZone).DateTime)
            .AddDays(-1);
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(range.End, _clubTimeZone).DateTime)
            .AddDays(1);

        for (var date = first; date <= last; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private enum Ambiguity
    {
        /// <summary>Bei doppelt vorhandener Stunde die frühere Ausprägung wählen.</summary>
        Earliest,

        /// <summary>Bei doppelt vorhandener Stunde die spätere Ausprägung wählen.</summary>
        Latest,
    }

    /// <summary>
    /// Bildet eine lokale Vereinszeit auf die absolute Zeitachse ab.
    ///
    /// Zweimal im Jahr ist diese Abbildung nicht eindeutig. In der Nacht der
    /// Vorstellung existiert eine Stunde gar nicht, in der Nacht der Rückstellung
    /// existiert sie doppelt. Beide Fälle liegen bei europäischen Zeitzonen
    /// nachts um drei und damit praktisch nie in den Öffnungszeiten eines
    /// Tennisvereins — sie stillschweigend falsch zu behandeln, wäre trotzdem ein
    /// Fehler, der genau einmal im Jahr für einen unerklärlichen Spielplan sorgt.
    ///
    /// Auflösung: Öffnungszeiten wandern auf die früheste, Schlusszeiten auf die
    /// späteste mögliche Ausprägung. Damit deckt das Fenster die doppelte Stunde
    /// vollständig ab, statt sie halb zu verlieren.
    /// </summary>
    private DateTimeOffset Resolve(DateOnly date, TimeOnly time, Ambiguity ambiguity)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        if (_clubTimeZone.IsInvalidTime(local))
        {
            return FirstValidInstantAfterGap(local);
        }

        if (_clubTimeZone.IsAmbiguousTime(local))
        {
            var offsets = _clubTimeZone.GetAmbiguousTimeOffsets(local);

            // Größerer UTC-Offset bedeutet früherer Zeitpunkt auf der Zeitachse.
            var offset = ambiguity == Ambiguity.Earliest ? offsets.Max() : offsets.Min();
            return new DateTimeOffset(local, offset);
        }

        return new DateTimeOffset(local, _clubTimeZone.GetUtcOffset(local));
    }

    /// <summary>
    /// Sucht den ersten gültigen Zeitpunkt nach einer übersprungenen Stunde.
    /// Zeitumstellungen betragen in der Praxis 30 bis 120 Minuten; die Obergrenze
    /// von vier Stunden ist reichlich bemessen und verhindert nur eine
    /// Endlosschleife bei einer absurden Zonendefinition.
    /// </summary>
    private DateTimeOffset FirstValidInstantAfterGap(DateTime local)
    {
        for (var minutes = 1; minutes <= 240; minutes++)
        {
            var candidate = local.AddMinutes(minutes);
            if (!_clubTimeZone.IsInvalidTime(candidate))
            {
                return new DateTimeOffset(candidate, _clubTimeZone.GetUtcOffset(candidate));
            }
        }

        throw new DomainException(
            $"In der Zeitzone {_clubTimeZone.Id} ließ sich zur lokalen Zeit {local:O} kein gültiger Zeitpunkt finden.");
    }
}
