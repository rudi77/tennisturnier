namespace TennisTurnier.Application.Common;

/// <summary>
/// Die angefragte Ressource existiert nicht — oder liegt außerhalb des Scopes des
/// Aufrufers. Beide Fälle sind hier bewusst nicht unterscheidbar (ADR-0004).
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string resource, Guid id)
        : base($"{resource} {id} wurde nicht gefunden.")
    {
    }

    /// <summary>
    /// Ohne Kennung in der Meldung.
    ///
    /// Für den Anmeldelink: sein Token steht in der Adresszeile und würde über
    /// die Fehlermeldung in <c>ProblemDetails</c> landen — und damit in jedem
    /// Protokoll, das Antworten mitschreibt. Es ist der Schlüssel zum Melden und
    /// gehört in keine Antwort, auch nicht in eine ablehnende.
    /// </summary>
    public NotFoundException(string resource)
        : base($"{resource} wurde nicht gefunden.")
    {
    }
}
