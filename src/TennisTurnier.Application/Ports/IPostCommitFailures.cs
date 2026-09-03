namespace TennisTurnier.Application.Ports;

/// <summary>
/// Wohin ein gescheiterter Nachlauf gemeldet wird.
///
/// Was nach dem Commit läuft, ist nicht mehr Teil des Schreibvorgangs: das
/// Ergebnis steht in der Datenbank, und ein Push, der nicht hinausgeht, ändert
/// daran nichts. Trotzdem darf er nicht spurlos verschwinden — der Zuschauer
/// im Vereinsheim sieht dann bis zum nächsten Abruf einen alten Stand, und
/// niemand wüsste, warum.
///
/// Ein eigener Port und kein <c>ILogger</c>: die Anwendungsschicht kennt
/// bewusst keine Infrastruktur, auch keine Protokollierung. Wohin die Meldung
/// geht, entscheidet die Composition Root.
/// </summary>
public interface IPostCommitFailures
{
    /// <summary>
    /// Eine Handlung nach dem Commit ist gescheitert. Der Schreibvorgang selbst
    /// war erfolgreich und bleibt es.
    /// </summary>
    void Report(Exception cause);
}
