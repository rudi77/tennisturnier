using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Identity.Oidc;

/// <summary>
/// Übersetzt das Token in einen <see cref="UserPrincipal"/>: lokales Konto suchen
/// oder anlegen, Rollenzuweisungen laden.
///
/// Muss nach <c>UseAuthentication</c> und vor allem, was Vereinsdaten abfragt,
/// laufen — der Query-Filter aus ADR-0004 wertet das Ergebnis aus.
/// </summary>
public sealed class UserResolutionMiddleware : IMiddleware
{
    private readonly IUserContext _userContext;
    private readonly IUserDirectory _directory;
    private readonly ILogger<UserResolutionMiddleware> _logger;

    public UserResolutionMiddleware(
        IUserContext userContext,
        IUserDirectory directory,
        ILogger<UserResolutionMiddleware> logger)
    {
        _userContext = userContext;
        _directory = directory;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (_userContext is ScopedUserContext scoped && context.User.Identity?.IsAuthenticated == true)
        {
            var principal = await ResolveAsync(context.User, context.RequestAborted);
            if (principal is not null)
            {
                scoped.Set(principal);
            }
        }

        await next(context);
    }

    private async Task<UserPrincipal?> ResolveAsync(ClaimsPrincipal claims, CancellationToken cancellationToken)
    {
        // MapInboundClaims ist abgeschaltet, die Claims tragen also ihre
        // ursprünglichen Namen aus dem Token.
        var subject = claims.FindFirst("sub");
        if (subject is null)
        {
            _logger.LogWarning("Token ohne sub-Claim erhalten; Aufruf bleibt anonym.");
            return null;
        }

        var issuer = claims.FindFirst("iss")?.Value ?? subject.Issuer;

        var account = await _directory.EnsureAccountAsync(
            issuer,
            subject.Value,
            claims.FindFirst("email")?.Value,
            claims.FindFirst("name")?.Value ?? claims.FindFirst("preferred_username")?.Value,
            cancellationToken);

        var assignments = await _directory.GetAssignmentsAsync(account.Id, cancellationToken);

        return new UserPrincipal(account.Id, assignments);
    }
}
