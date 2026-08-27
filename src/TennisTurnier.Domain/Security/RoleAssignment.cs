using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Security;

/// <summary>
/// Bindet eine Rolle an einen Benutzer und eine Ressource.
///
/// Diese Zuordnung liegt bewusst in der Anwendung und nicht beim Identity
/// Provider (ADR-0007): der IdP kennt die Vereine nicht und müsste den Scope in
/// einen Claim-Wert kodieren.
/// </summary>
public sealed class RoleAssignment : Entity
{
    public RoleAssignment(Guid id, Guid userId, Role role, ResourceScope scope)
        : base(id)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Eine Rollenzuweisung braucht einen Benutzer.");
        }

        if (scope.Type != ExpectedScopeOf(role))
        {
            throw new DomainException(
                $"Die Rolle {role} gilt im Scope {ExpectedScopeOf(role)}, angegeben war {scope.Type}.");
        }

        UserId = userId;
        Role = role;
        Scope = scope;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private RoleAssignment(Guid id) : base(id)
    {
    }

    public Guid UserId { get; private set; }

    public Role Role { get; private set; }

    public ResourceScope Scope { get; private set; }

    /// <summary>
    /// Prüft, ob diese Zuweisung die Berechtigung in einem der angefragten Scopes
    /// gewährt. Ein SystemAdmin gewährt sie in jedem Scope.
    /// </summary>
    public bool Grants(Permission permission, IReadOnlyCollection<ResourceScope> scopes)
    {
        if (!Permissions.Of(Role).Contains(permission))
        {
            return false;
        }

        return Role == Role.SystemAdmin || scopes.Contains(Scope);
    }

    private static ScopeType ExpectedScopeOf(Role role) => role switch
    {
        Role.SystemAdmin => ScopeType.Global,
        Role.Organizer => ScopeType.Global,
        Role.TournamentDirector => ScopeType.Tournament,
        Role.Referee => ScopeType.Tournament,
        Role.Member => ScopeType.Tournament,
        _ => throw new DomainException($"Unbekannte Rolle {role}."),
    };

    public override string ToString() => $"{Role}@{Scope}";
}
