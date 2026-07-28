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
        Resource = resource;
        Id = id;
    }

    public string Resource { get; }

    public Guid Id { get; }
}
