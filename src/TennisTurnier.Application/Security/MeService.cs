using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Security;

public interface IMeService
{
    /// <summary>
    /// Der angemeldete Benutzer samt seiner Rollen. Leer, wenn niemand
    /// angemeldet ist — der Endpunkt ist die Frage „wer bin ich", und darauf
    /// ist „niemand" eine gültige Antwort und kein Fehler.
    /// </summary>
    Task<MeResponse?> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class MeService : IMeService
{
    private readonly IUserContext _userContext;
    private readonly IUserDirectory _directory;

    public MeService(IUserContext userContext, IUserDirectory directory)
    {
        _userContext = userContext;
        _directory = directory;
    }

    public async Task<MeResponse?> GetAsync(CancellationToken cancellationToken = default)
    {
        var user = _userContext.Current;

        if (!user.IsAuthenticated)
        {
            return null;
        }

        var account = await _directory.FindAsync(user.UserId, cancellationToken);

        return new MeResponse(
            user.UserId,
            account?.DisplayName,
            account?.Email,
            user.IsSystemAdmin,
            [.. user.Assignments.Select(a =>
                new RoleAssignmentSummary(a.Id, a.Role, a.Scope.Type, a.Scope.ResourceId))]);
    }
}
