using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Security;

/// <summary>Wer an einem Turnier welche Rolle hat.</summary>
public sealed record TournamentRoleSummary(
    Guid AssignmentId,
    Guid UserId,
    string? DisplayName,
    string? Email,
    Role Role);

/// <param name="Email">
/// Die Adresse eines <em>bestehenden</em> Kontos. Berufen lässt sich nur, wer
/// sich schon einmal angemeldet hat — die Einladung eines noch nicht
/// angemeldeten Benutzers bleibt ein offener Punkt (ADR-0007).
/// </param>
public sealed record GrantRoleRequest(string Email, Role Role);

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
///    <see cref="Role.TournamentDirector"/> und <see cref="Role.Referee"/> im
///    Scope dieses Turniers. Ein Turnierleiter, der eine globale Rolle vergeben
///    könnte, machte sich selbst zum Systemadministrator — über den Umweg eines
///    zweiten Kontos, das ihm gehört.
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

    Task<Guid> GrantAsync(
        Guid tournamentId,
        GrantRoleRequest request,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid tournamentId, Guid assignmentId, CancellationToken cancellationToken = default);
}

public sealed class RoleService : IRoleService
{
    private readonly ITournamentRepository _tournaments;
    private readonly IRoleAssignmentRepository _roles;
    private readonly IUserDirectory _directory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;

    public RoleService(
        ITournamentRepository tournaments,
        IRoleAssignmentRepository roles,
        IUserDirectory directory,
        IUnitOfWork unitOfWork,
        IUserContext userContext)
    {
        _tournaments = tournaments;
        _roles = roles;
        _directory = directory;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
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
                    a.Role))
        ];
    }

    public async Task<Guid> GrantAsync(
        Guid tournamentId,
        GrantRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await RequireManagementAsync(tournamentId, cancellationToken);
        RequireAssignableRole(request.Role);

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new DomainException("Berufen wird über die E-Mail-Adresse eines bestehenden Kontos.");
        }

        var account = await _directory.FindByEmailAsync(request.Email.Trim(), cancellationToken)
            ?? throw new DomainException(
                $"Zu „{request.Email.Trim()}“ gibt es kein Konto. Berufen lässt sich nur, wer sich " +
                "schon einmal angemeldet hat.");

        var existing = await _roles.ListByTournamentAsync(tournamentId, cancellationToken);

        // Idempotent: dieselbe Rolle noch einmal zu vergeben ist keine
        // Änderung, sondern der zweite Klick auf dieselbe Schaltfläche.
        if (existing.FirstOrDefault(a => a.UserId == account.Id && a.Role == request.Role) is { } already)
        {
            return already.Id;
        }

        var assignment = new RoleAssignment(
            Guid.NewGuid(), account.Id, request.Role, ResourceScope.Tournament(tournamentId));

        _roles.Add(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return assignment.Id;
    }

    public async Task RevokeAsync(
        Guid tournamentId,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        await RequireManagementAsync(tournamentId, cancellationToken);

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
        if (role is not (Role.TournamentDirector or Role.Referee))
        {
            throw new DomainException(
                $"An einem Turnier lassen sich nur {Role.TournamentDirector} und {Role.Referee} " +
                $"vergeben, angefragt war {role}.");
        }
    }
}
