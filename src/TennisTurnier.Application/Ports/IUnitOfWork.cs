namespace TennisTurnier.Application.Ports;

/// <summary>
/// Schließt eine Änderungsgruppe ab. Anwendungsfälle sammeln ihre Änderungen an
/// den Aggregaten und schreiben sie am Ende in einem Zug — nicht Feld für Feld.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
