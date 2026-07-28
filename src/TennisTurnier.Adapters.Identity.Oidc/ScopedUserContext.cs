using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Identity.Oidc;

/// <summary>
/// Hält den aufgelösten Benutzer für die Dauer eines Requests.
///
/// <see cref="IUserContext"/> ist bewusst synchron, weil der Query-Filter im
/// DbContext ihn zur Abfragezeit auswertet und dort nicht auf eine Datenbank
/// warten kann. Die Auflösung selbst passiert deshalb einmal vorab in
/// <see cref="UserResolutionMiddleware"/>.
/// </summary>
internal sealed class ScopedUserContext : IUserContext
{
    public UserPrincipal Current { get; private set; } = UserPrincipal.Anonymous;

    public void Set(UserPrincipal principal) => Current = principal;
}
