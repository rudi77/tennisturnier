namespace TennisTurnier.Domain.Common;

/// <summary>
/// Verletzung einer fachlichen Regel. Wird an der API-Grenze auf 422 abgebildet —
/// im Unterschied zu einem Programmierfehler, der als 500 durchschlägt.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
