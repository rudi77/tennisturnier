using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Security;

/// <summary>
/// Wer an einem Turnier welche Rolle hat — und wer eine bekommen soll, sobald
/// er sich zum ersten Mal anmeldet.
/// </summary>
/// <param name="AssignmentId">
/// Bei einer offenen Einladung die Kennung der Einladung. Beide werden über
/// denselben Weg zurückgenommen, und für die Oberfläche ist es dieselbe Zeile
/// mit demselben Knopf daneben.
/// </param>
/// <param name="UserId">Leer, solange es das Konto noch nicht gibt.</param>
/// <param name="Pending">
/// Eingeladen, aber noch nie angemeldet. Die Turnierleitung soll den
/// Unterschied sehen: der eine ist dabei, auf den anderen wartet man.
/// </param>
public sealed record TournamentRoleSummary(
    Guid AssignmentId,
    Guid UserId,
    string? DisplayName,
    string? Email,
    Role Role,
    bool Pending = false);

/// <param name="Email">
/// Die Adresse. Gibt es dazu ein Konto, bekommt es die Rolle sofort; sonst
/// wartet eine Einladung darauf, beim ersten Login eingelöst zu werden
/// (ADR-0007, ADR-0012).
/// </param>
public sealed record GrantRoleRequest(string Email, Role Role);

/// <param name="Invited">
/// Wahr, wenn es zu der Adresse noch kein Konto gab. Dann ist nichts
/// geschehen, was der Eingeladene schon merken könnte — die Turnierleitung
/// muss ihm den Weg selbst schicken.
/// </param>
public sealed record GrantRoleResult(Guid Id, bool Invited);

/// <summary>
/// Schiedsrichter und weitere Turnierleiter berufen und entziehen.
///
/// Der Punkt, an dem eine frische Instanz bisher stehenblieb: Rollen vergibt,
/// wer eine Rolle hat, und einen Endpunkt dafür gab es nicht. Für die erste
/// Rolle sorgen inzwischen <see cref="SystemAdminBootstrap"/> und
/// <see cref="OrganizerBootstrap"/>; alles Weitere geht über diesen
/// Anwendungsfall.
///
/// Zwei Regeln tragen ihn, und beide sind Sperren:
///
///  - **Keine Eskalation.** Erlaubt sind ausschließlich
///    <see cref="Role.TournamentDirector"/>, <see cref="Role.Referee"/> und
///    <see cref="Role.Member"/> im Scope dieses Turniers. Ein Turnierleiter,
///    der eine globale Rolle vergeben könnte, machte sich selbst zum
///    Systemadministrator — über den Umweg eines zweiten Kontos, das ihm
///    gehört.
///  - **Kein herrenloses Turnier.** Die letzte Turnierleiter-Zuweisung lässt
///    sich nicht entfernen. Ohne sie sähe niemand mehr das Turnier, und weil
///    der Query-Filter keinen zweiten Weg dorthin kennt, gäbe es auch keinen
///    zurück.
/// </summary>
public interface IRoleService
{
    Task<IReadOnlyList<TournamentRoleSummary>> ListAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default);

    Task<GrantRoleResult> GrantAsync(
        Guid tournamentId,
        GrantRoleRequest request,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid tournamentId, Guid assignmentId, CancellationToken cancellationToken = default);
}

public sealed class RoleService : IRoleService
{
    private readonly ITournamentRepository _tournaments;
    private readonly IRoleAssignmentRepository _roles;
    private readonly IInvitationRepository _invitations;
    private readonly IUserDirectory _directory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public RoleService(
        ITournamentRepository tournaments,
        IRoleAssignmentRepository roles,
        IInvitationRepository invitations,
        IUserDirectory directory,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _tournaments = tournaments;
        _roles = roles;
        _invitations = invitations;
        _directory = directory;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TournamentRoleSummary>> ListAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default)
    {
        await RequireManagementAsync(tournamentId, cancellationToken);

        var assignments = await _roles.ListByTournamentAsync(tournamentId, cancellationToken);
        var accounts = await _directory.FindManyAsync(
            [.. assignments.Select(a => a.UserId).Distinct()], cancellationToken);

        var byId = accounts.ToDictionary(a => a.Id);

        var invitations = await _invitations.ListByTournamentAsync(tournamentId, cancellationToken);

        return
        [
            .. assignments
                .OrderBy(a => a.Role)
                // Zu jeder Zuweisung gibt es ihr Konto: der Fremdschlüssel
                // lässt keine ohne zu, und gelöscht wird sie mit ihm.
                .Select(a => new TournamentRoleSummary(
                    a.Id,
                    a.UserId,
                    byId[a.UserId].DisplayName,
                    byId[a.UserId].Email,
                    a.Role)),

            // Danach, wer noch nicht da ist. In einer Liste und nicht in einer
            // zweiten daneben: für die Turnierleitung ist es dieselbe Frage —
            // wer gehört zu diesem Turnier — und die Antwort „noch nicht
            // angemeldet" gehört in dieselbe Zeile.
            .. invitations
                .OrderBy(i => i.Role)
                .ThenBy(i => i.Email, StringComparer.Ordinal)
                .Select(i => new TournamentRoleSummary(
                    i.Id,
                    Guid.Empty,
                    DisplayName: null,
                    i.Email,
                    i.Role,
                    Pending: true)),
        ];
    }

    /// <summary>
    /// Vergibt die Rolle — oder legt eine Einladung an, wenn es zu der Adresse
    /// noch kein Konto gibt.
    ///
    /// Bis hierher endete dieser Weg an einer Fehlermeldung: „berufen lässt
    /// sich nur, wer sich schon einmal angemeldet hat". Sie war richtig und
    /// trotzdem eine Sackgasse — wer jemanden einladen wollte, musste ihn
    /// zuerst dazu bringen, sich anzumelden, ohne ihm sagen zu können, wofür.
    /// </summary>
    public async Task<GrantRoleResult> GrantAsync(
        Guid tournamentId,
        GrantRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await RequireManagementAsync(tournamentId, cancellationToken);
        RequireAssignableRole(request.Role);

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new DomainException("Eingeladen wird über eine E-Mail-Adresse.");
        }

        var email = request.Email.Trim();

        return await _directory.FindByEmailAsync(email, cancellationToken) is { } account
            ? new GrantRoleResult(
                await AssignAsync(tournamentId, account.Id, request.Role, cancellationToken),
                Invited: false)
            : new GrantRoleResult(
                await InviteAsync(tournamentId, email, request.Role, cancellationToken),
                Invited: true);
    }

    private async Task<Guid> AssignAsync(
        Guid tournamentId,
        Guid userId,
        Role role,
        CancellationToken cancellationToken)
    {
        var existing = await _roles.ListByTournamentAsync(tournamentId, cancellationToken);

        // Idempotent: dieselbe Rolle noch einmal zu vergeben ist keine
        // Änderung, sondern der zweite Klick auf dieselbe Schaltfläche.
        if (existing.FirstOrDefault(a => a.UserId == userId && a.Role == role) is { } already)
        {
            return already.Id;
        }

        var assignment = new RoleAssignment(
            Guid.NewGuid(), userId, role, ResourceScope.Tournament(tournamentId));

        _roles.Add(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return assignment.Id;
    }

    private async Task<Guid> InviteAsync(
        Guid tournamentId,
        string email,
        Role role,
        CancellationToken cancellationToken)
    {
        var offen = await _invitations.ListByTournamentAsync(tournamentId, cancellationToken);

        if (offen.FirstOrDefault(i =>
            string.Equals(i.Email, email, StringComparison.OrdinalIgnoreCase)
            && i.Role == role) is { } bereits)
        {
            return bereits.Id;
        }

        var invitation = new Invitation(Guid.NewGuid(), tournamentId, email, role, _clock.Now);

        _invitations.Add(invitation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return invitation.Id;
    }

    /// <summary>
    /// Nimmt eine Rolle zurück — oder eine Einladung, die noch auf ihr Konto
    /// wartet. Für die Turnierleitung ist beides derselbe Knopf an derselben
    /// Zeile, und deshalb derselbe Endpunkt.
    /// </summary>
    public async Task RevokeAsync(
        Guid tournamentId,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        await RequireManagementAsync(tournamentId, cancellationToken);

        var invitations = await _invitations.ListByTournamentAsync(tournamentId, cancellationToken);

        if (invitations.FirstOrDefault(i => i.Id == assignmentId) is { } invitation)
        {
            // Keine Prüfung auf die letzte Turnierleitung: eine Einladung führt
            // niemanden, sie wartet nur.
            _invitations.Remove(invitation);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var assignments = await _roles.ListByTournamentAsync(tournamentId, cancellationToken);

        var assignment = assignments.FirstOrDefault(a => a.Id == assignmentId)
            ?? throw new NotFoundException("Rollenzuweisung", assignmentId);

        // Ein Turnier ohne Turnierleiter wäre für niemanden mehr sichtbar. Das
        // ist keine Unbequemlichkeit, sondern eine Einbahnstraße: der
        // Query-Filter kennt keinen zweiten Weg zu einem Turnier, und ohne
        // Sicht darauf lässt sich auch keine Rolle daran vergeben.
        if (assignment.Role == Role.TournamentDirector
            && assignments.Count(a => a.Role == Role.TournamentDirector) == 1)
        {
            throw new DomainException(
                "Das ist die letzte Turnierleitung. Ohne sie sähe niemand mehr dieses Turnier — " +
                "zuerst jemand anderen berufen.");
        }

        _roles.Remove(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Erst laden, dann prüfen: ein Turnier außerhalb des Scopes endet damit als
    /// 404 und nicht als 403 — ein 403 verriete seine Existenz (ADR-0004).
    /// </summary>
    private async Task RequireManagementAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        _ = await _tournaments.FindAsync(tournamentId, cancellationToken)
            ?? throw new NotFoundException("Turnier", tournamentId);

        _userContext.Current.Require(Permission.ManageTournament, ResourceScope.Tournament(tournamentId));
    }

    /// <summary>
    /// Die Eskalationssperre.
    ///
    /// Sie steht hier und nicht bloß im Vertrauen auf
    /// <c>RoleAssignment.ExpectedScopeOf</c>: dort scheiterte eine globale
    /// Rolle im Turnierscope zwar ebenfalls, aber mit der Begründung „falscher
    /// Scope". Das liest sich wie ein Tippfehler und nicht wie das, was es ist.
    /// </summary>
    private static void RequireAssignableRole(Role role)
    {
        if (role is not (Role.TournamentDirector or Role.Referee or Role.Member))
        {
            throw new DomainException(
                $"An einem Turnier lassen sich nur {Role.TournamentDirector}, {Role.Referee} " +
                $"und {Role.Member} vergeben, angefragt war {role}.");
        }
    }
}
