namespace TennisTurnier.Application.Ports;

/// <summary>
/// Die aktuelle Zeit als Port.
///
/// Ein Turnier ist voller Zeitbezüge, und ein Test, der auf die Systemuhr
/// wartet, ist kein Test. Alles in UTC — die Vereinszeitzone gehört an die
/// Anzeige, nicht an die Speicherung.
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}
