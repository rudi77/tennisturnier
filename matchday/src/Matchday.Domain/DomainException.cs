namespace Matchday.Domain;

/// <summary>Eine fachliche Regel wurde verletzt. Die Meldung ist für Menschen geschrieben.</summary>
public sealed class DomainException(string message) : Exception(message);
