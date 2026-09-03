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

    /// <summary>
    /// Die Herkunft des Kontos im offenen Betrieb.
    ///
    /// Kein Aussteller, sondern eine Kennzeichnung: sie kann mit keiner echten
    /// Herkunft kollidieren, und sie steht in der Datenbank, wo man sie später
    /// wiederfindet — samt allem, was unter ihr angelegt wurde.
    /// </summary>
    internal const string OpenAccessIssuer = "matchday:ohne-anmeldung";

    internal const string OpenAccessSubject = "offener-betrieb";

    private readonly ScopedUserContext _userContext;
    private readonly IUserDirectory _directory;
    private readonly SystemAdminBootstrap _bootstrap;
    private readonly OrganizerBootstrap _organizers;
    private readonly InvitationRedemption _invitations;
    private readonly BootstrapAdminOptions _options;
    private readonly OidcOptions _oidc;
    private readonly ILogger<UserResolutionMiddleware> _logger;

    public UserResolutionMiddleware(
        ScopedUserContext userContext,
        IUserDirectory directory,
        SystemAdminBootstrap bootstrap,
        OrganizerBootstrap organizers,
        InvitationRedemption invitations,
        BootstrapAdminOptions options,
        OidcOptions oidc,
        ILogger<UserResolutionMiddleware> logger)
    {
        _userContext = userContext;
        _directory = directory;
        _bootstrap = bootstrap;
        _organizers = organizers;
        _invitations = invitations;
        _options = options;
        _oidc = oidc;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // HttpContext.User trägt immer eine Identität — auch ohne Anmeldung,
        // dann eben eine unauthentifizierte.
        if (context.User.Identity!.IsAuthenticated)
        {
            var principal = await ResolveAsync(context.User, context.RequestAborted);
            if (principal is not null)
            {
                _userContext.Set(principal);
            }
        }
        else if (_options.OpenAccess)
        {
            _userContext.Set(await ResolveOpenAccessAsync(context.RequestAborted));
        }

        await next(context);
    }

    /// <summary>
    /// Der eine Benutzer, als der im offenen Betrieb jeder gilt.
    ///
    /// Ein echtes Konto und keine Sonderfassung des Aufrufers: die Turniere
    /// brauchen einen Eigentümer, der Query-Filter aus ADR-0004 einen Benutzer,
    /// und beides soll denselben Weg gehen wie mit Anmeldung. Wird die
    /// Anmeldung später eingeschaltet, bleibt das Konto stehen — anmelden kann
    /// sich niemand mehr als es, und ein Systemadministrator kann ihm die Rolle
    /// nehmen.
    /// </summary>
    private async Task<UserPrincipal> ResolveOpenAccessAsync(CancellationToken cancellationToken)
    {
        var account = await _directory.EnsureAccountAsync(
            OpenAccessIssuer,
            OpenAccessSubject,
            email: null,
            displayName: "Ohne Anmeldung",
            cancellationToken);

        var assignments = await _directory.GetAssignmentsAsync(account.Id, cancellationToken);

        if (!assignments.Any(a => a.Role == Role.SystemAdmin))
        {
            await _directory.AssignAsync(
                new RoleAssignment(Guid.NewGuid(), account.Id, Role.SystemAdmin, ResourceScope.Global),
                cancellationToken);

            // Als Warnung: eine Instanz, die jedem alles erlaubt, soll in einem
            // auf Information gefilterten Protokoll auffallen — und zwar dort,
            // wo es zum ersten Mal tatsächlich passiert.
            _logger.LogWarning(
                "Offener Betrieb: Konto {UserId} gilt für jeden Aufruf und ist Systemadministrator. "
                + "Wer die Adresse kennt, darf alles.",
                account.Id);

            assignments = await _directory.GetAssignmentsAsync(account.Id, cancellationToken);
        }

        return new UserPrincipal(account.Id, assignments);
    }

    /// <summary>
    /// Die E-Mail-Adresse aus dem Token — aber nur, wenn der Aussteller sie
    /// bestätigt hat.
    ///
    /// Sie ist kein Anzeigename, sondern ein Schlüssel: an ihr hängen der erste
    /// Systemadministrator, jede Einladung an eine noch nicht angemeldete
    /// Adresse und die Übernahme eines importierten Spielers samt Kontaktdaten
    /// und Historie. Ein Aussteller mit offener Selbstregistrierung lässt die
    /// Adresse frei wählen — wer sich vor ihrem Inhaber anmeldet, bekäme sonst,
    /// was für diesen hinterlegt wurde.
    ///
    /// Ein fehlender Claim zählt wie ein verneinter: „der Aussteller sagt nichts
    /// dazu" ist keine Bestätigung. Wer einen Aussteller betreibt, der den Claim
    /// nicht ausstellt und trotzdem nur bestätigte Adressen herausgibt, sagt das
    /// über <see cref="OidcOptions.TrustUnverifiedEmail"/> ausdrücklich.
    /// </summary>
    private string? BestaetigteEmail(ClaimsPrincipal claims)
    {
        var email = claims.FindFirst("email")?.Value;

        if (string.IsNullOrWhiteSpace(email) || _oidc.TrustUnverifiedEmail)
        {
            return email;
        }

        // Als Zeichenkette und nicht als bool: der Claim kommt aus JSON, und je
        // nach Aussteller steht dort true oder "true".
        if (string.Equals(claims.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return email;
        }

        _logger.LogInformation(
            "Token trägt eine unbestätigte E-Mail-Adresse; sie wird nicht übernommen. "
            + "Wenn der Aussteller den Claim email_verified nicht ausstellt, ist "
            + "Oidc:TrustUnverifiedEmail der dafür vorgesehene Schalter.");

        return null;
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
            BestaetigteEmail(claims),
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

        // Zuletzt, was jemand anderes für dieses Konto hinterlegt hat, bevor es
        // das Konto gab: die Einladungen an seine Adresse. Sie kommen nach den
        // beiden Bootstraps, weil sie deren Ergebnis brauchen — wer hier schon
        // eine Rolle am Turnier hat, bekommt keine zweite.
        if (await _invitations.ApplyAsync(account, cancellationToken))
        {
            _logger.LogInformation(
                "Konto {UserId} ({Subject}) hat offene Einladungen eingelöst.",
                account.Id,
                account.SubjectId);

            assignments = await _directory.GetAssignmentsAsync(account.Id, cancellationToken);
        }

        return new UserPrincipal(account.Id, assignments);
    }
}
