using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Der aufrufende Benutzer für die Dauer eines Requests.
///
/// Liefert nie <c>null</c>: ohne Anmeldung ist es <see cref="UserPrincipal.Anonymous"/>.
/// Der Query-Filter aus ADR-0004 hängt daran und braucht eine totale Antwort.
/// </summary>
public interface IUserContext
{
    UserPrincipal Current { get; }
}
