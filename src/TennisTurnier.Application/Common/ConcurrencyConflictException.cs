namespace TennisTurnier.Application.Common;

/// <summary>
/// Jemand anderes war schneller: die Ressource wurde geändert, seit der Aufrufer
/// sie gelesen hat.
///
/// Zwei Schiedsrichter am selben Match sind am Turniertag der Normalfall, nicht
/// der Ausnahmefall. Deshalb ist das ein eigener Fehler mit eigener Antwort
/// (409) und nicht ein unbehandelter Serverfehler — der Aufrufer soll neu laden
/// und es noch einmal versuchen, statt einen Defekt zu melden.
///
/// Der Adapter, der den Konflikt erkennt, übersetzt ihn hierher. So bleibt die
/// Kenntnis der Persistenztechnik dort, wo sie hingehört.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception innerException)
        : base(
            "Die Daten wurden zwischenzeitlich von jemand anderem geändert. "
            + "Bitte neu laden und die Änderung wiederholen.",
            innerException)
    {
    }
}
