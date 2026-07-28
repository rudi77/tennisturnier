using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

internal static class ApiFactoryExtensions
{
    /// <summary>
    /// Legt das Konto zum Testbenutzer an, falls nötig, und weist ihm eine Rolle zu.
    ///
    /// Läuft über dieselben Ports wie die Anwendung — die Rollenvergabe hat noch
    /// keinen Endpunkt, weil ADR-0007 die Einladung ungemeldeter Benutzer bewusst
    /// offenlässt.
    /// </summary>
    public static async Task GrantAsync(
        this TennisTurnierApiFactory factory,
        string subject,
        Role role,
        ResourceScope scope)
    {
        await using var services = factory.Services.CreateAsyncScope();
        var directory = services.ServiceProvider.GetRequiredService<IUserDirectory>();

        var account = await directory.EnsureAccountAsync(
            TennisTurnierApiFactory.TestIssuer,
            subject,
            email: null,
            displayName: subject);

        await directory.AssignAsync(new RoleAssignment(Guid.NewGuid(), account.Id, role, scope));
    }
}
