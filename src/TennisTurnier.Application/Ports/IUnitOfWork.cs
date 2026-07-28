namespace TennisTurnier.Application.Ports;

/// <summary>
/// Schließt eine Änderungsgruppe ab. Anwendungsfälle sammeln ihre Änderungen an
/// den Aggregaten und schreiben sie am Ende in einem Zug — nicht Feld für Feld.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Schließt die Einheit ab: schreibt, macht die Änderungen sichtbar und löst
    /// aus, was auf das Speichern gewartet hat.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Schreibt die bisherigen Änderungen, ohne die Einheit abzuschließen.
    ///
    /// Gebraucht wird das, wo ein Anwendungsfall etwas anlegt und im selben Zug
    /// wieder lesen muss: eine neu angelegte Phase steht noch nirgends, wo eine
    /// Abfrage sie fände, und die öffentliche Ansicht bliebe leer. Alles
    /// Geschriebene wird erst mit <see cref="SaveChangesAsync"/> endgültig —
    /// scheitert der Abschluss, ist auch das hier Geschriebene wieder weg.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
