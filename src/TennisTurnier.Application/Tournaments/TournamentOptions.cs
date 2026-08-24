namespace TennisTurnier.Application.Tournaments;

/// <summary>
/// Einstellungen, die für alle Turniere einer Instanz gelten.
///
/// Sie stehen in der Konfiguration und nicht am einzelnen Turnier: es sind
/// Entscheidungen des Betreibers über seine Instanz, keine der Turnierleitung
/// über ihre Ausschreibung.
/// </summary>
public sealed class TournamentOptions
{
    public const string SectionName = "Tournament";

    /// <summary>
    /// Der Saatwert für das Los der Teams — leer für echten Zufall.
    ///
    /// Mit Saatwert ergibt dieselbe Meldungsliste immer dieselben Teams. Das
    /// ist der Unterschied zwischen „ausgelost" und „nachvollziehbar
    /// ausgelost": eine Vorführung, eine Testumgebung und die Frage „wie kam
    /// diese Paarung zustande" brauchen einen Ablauf, der sich wiederholen
    /// lässt.
    ///
    /// Für ein Turnier, bei dem tatsächlich um etwas gelost wird, gehört er
    /// leer: wer den Saatwert kennt, kennt die Paarung, bevor sie fällt.
    /// </summary>
    public int? TeamDrawSeed { get; set; }
}
