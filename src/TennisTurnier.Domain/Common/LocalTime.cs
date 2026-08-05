namespace TennisTurnier.Domain.Common;

/// <summary>
/// Bildet lokale Wanduhrzeit auf die absolute Zeitachse ab.
///
/// Zweimal im Jahr ist diese Abbildung nicht eindeutig. In der Nacht der
/// Vorstellung existiert eine Stunde gar nicht, in der Nacht der Rückstellung
/// existiert sie doppelt. Beide Fälle liegen bei europäischen Zeitzonen nachts
/// um drei und damit praktisch nie in den Platzzeiten eines Tennisturniers — sie
/// stillschweigend falsch zu behandeln, wäre trotzdem ein Fehler, der genau
/// einmal im Jahr für einen unerklärlichen Spielplan sorgt.
///
/// Die Rechnung steht hier und nicht bei ihren Aufrufern, weil sie sonst an
/// zwei Stellen stünde: der Veranstalter gibt seine Platzzeiten als lokale
/// Datum-Uhrzeit-Paare an, und der Turnierzeitraum wird aus lokalen
/// Kalendertagen gebildet. Zwei Fassungen derselben Umrechnung wären zwei
/// Antworten auf die Frage, wann ein Turniertag beginnt.
/// </summary>
public sealed class LocalTime
{
    private readonly TimeZoneInfo _zone;

    public LocalTime(TimeZoneInfo zone) =>
        _zone = zone ?? throw new ArgumentNullException(nameof(zone));

    /// <summary>Welche Ausprägung einer doppelt vorhandenen Stunde gemeint ist.</summary>
    public enum Ambiguity
    {
        /// <summary>Die frühere — die Wahl für einen Beginn.</summary>
        Earliest,

        /// <summary>Die spätere — die Wahl für ein Ende.</summary>
        Latest,
    }

    /// <summary>
    /// Mitternacht des lokalen Kalendertags. Der Anfang eines Turniertags,
    /// nicht der Anfang eines UTC-Tags.
    /// </summary>
    public DateTimeOffset Midnight(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);

        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
    }

    /// <summary>
    /// Der Zeitpunkt zu einer lokalen Datum-Uhrzeit-Angabe.
    ///
    /// Beginn und Ende eines Fensters lösen eine doppelte Stunde
    /// gegenläufig auf: der Beginn wandert auf die früheste, das Ende auf die
    /// späteste Ausprägung. Damit deckt das Fenster die doppelte Stunde
    /// vollständig ab, statt sie halb zu verlieren.
    /// </summary>
    public DateTimeOffset Resolve(DateOnly date, TimeOnly time, Ambiguity ambiguity)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        if (_zone.IsInvalidTime(local))
        {
            return FirstValidInstantAfterGap(local);
        }

        if (_zone.IsAmbiguousTime(local))
        {
            var offsets = _zone.GetAmbiguousTimeOffsets(local);

            // Größerer UTC-Offset bedeutet früherer Zeitpunkt auf der Zeitachse.
            var offset = ambiguity == Ambiguity.Earliest ? offsets.Max() : offsets.Min();
            return new DateTimeOffset(local, offset);
        }

        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
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
            if (!_zone.IsInvalidTime(candidate))
            {
                return new DateTimeOffset(candidate, _zone.GetUtcOffset(candidate));
            }
        }

        throw new DomainException(
            $"In der Zeitzone {_zone.Id} ließ sich zur lokalen Zeit {local:O} kein gültiger Zeitpunkt finden.");
    }
}
