using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.Security;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Identity.Oidc;

/// <summary>
/// Übersetzt das Token in einen <see cref="UserPrincipal"/>: lokales Konto suchen
/// oder anlegen, Rollenzuweisungen laden.
///
/// Muss nach <c>UseAuthentication</c> und vor allem, was Vereinsdaten abfragt,
/// laufen — der Query-Filter aus ADR-0004 wertet das Ergebnis aus.
/// </summary>
internal sealed class UserResolutionMiddleware : IMiddleware
{
    /// <summary>Der Konfigurationsschlüssel, wie er in appsettings.json steht.</summary>
    private const string Setting =
        $"{BootstrapAdminOptions.SectionName}:{nameof(BootstrapAdminOptions.BootstrapSystemAdmins)}";

    private readonly ScopedUserContext _userContext;
    private readonly IUserDirectory _directory;
    private readonly SystemAdminBootstrap _bootstrap;
    private readonly OrganizerBootstrap _organizers;
    private readonly ILogger<UserResolutionMiddleware> _logger;

    public UserResolutionMiddleware(
        ScopedUserContext userContext,
        IUserDirectory directory,
        SystemAdminBootstrap bootstrap,
        OrganizerBootstrap organizers,
        ILogger<UserResolutionMiddleware> logger)
    {
        _userContext = userContext;
        _directory = directory;
        _bootstrap = bootstrap;
        _organizers = organizers;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var principal = await ResolveAsync(context.User, context.RequestAborted);
            if (principal is not null)
            {
                _userContext.Set(principal);
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

        // Erst hier, nicht beim Start: vorher gibt es das Konto nicht, dem die
        // Rolle gehören soll.
        switch (await _bootstrap.ApplyAsync(account, assignments, cancellationToken))
        {
            case BootstrapOutcome.Granted:
                // Als Warnung, obwohl nichts schiefging: die Vergabe der höchsten
                // Rolle aus einer Konfigurationsdatei heraus soll in einem
                // Protokoll auffallen, das auf Information gefiltert ist.
                _logger.LogWarning(
                    "Konto {UserId} ({Subject}) wurde laut {Setting} zum Systemadministrator gemacht.",
                    account.Id,
                    account.SubjectId,
                    Setting);

                assignments = await _directory.GetAssignmentsAsync(account.Id, cancellationToken);
                break;

            case BootstrapOutcome.NotListed:
                // Sagt an, wonach die Konfiguration gesucht hat und was am Token
                // stand. Ohne diese Zeile bliebe eine E-Mail, die der Aussteller
                // gar nicht ins Token legt, ein stummer Fehlschlag — und der
                // Betreiber wartet auf eine Rolle, die nie kommt.
                _logger.LogInformation(
                    "Konto {UserId} steht nicht in {Setting}. Eingetragen werden kann E-Mail „{Email}\" oder Subject „{Subject}\".",
                    account.Id,
                    Setting,
                    account.Email ?? "(keine im Token)",
                    account.SubjectId);
                break;
        }

        // Und der Selbstservice: wer sich anmeldet, darf Turniere anlegen. Nach
        // dem Systemadministrator, damit er die Rolle nicht zusätzlich bekommt —
        // er darf ohnehin alles.
        if (await _organizers.ApplyAsync(account, assignments, cancellationToken))
        {
            _logger.LogInformation(
                "Konto {UserId} ({Subject}) wurde als Veranstalter freigeschaltet.",
                account.Id,
                account.SubjectId);

            assignments = await _directory.GetAssignmentsAsync(account.Id, cancellationToken);
        }

        return new UserPrincipal(account.Id, assignments);
    }
}
