using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Security;

/// <summary>
/// Der aufrufende Benutzer samt seiner Rollenzuweisungen — die Antwort auf jede
/// Zugriffsfrage in einem Objekt.
///
/// Es gibt ihn immer, auch ohne Anmeldung (<see cref="Anonymous"/>). Das macht
/// den Query-Filter aus ADR-0004 total: er muss keinen Sonderfall „kein Benutzer"
/// behandeln, und die öffentliche Ansicht läuft durch dieselbe Prüfung wie alles
/// andere.
/// </summary>
public sealed class UserPrincipal
{
    private readonly bool _isSystem;

    public UserPrincipal(Guid userId, IReadOnlyList<RoleAssignment> assignments)
        : this(userId, assignments, isSystem: false)
    {
    }

    private UserPrincipal(Guid userId, IReadOnlyList<RoleAssignment> assignments, bool isSystem)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        if (assignments.Any(a => a.UserId != userId))
        {
            throw new DomainException("Alle Rollenzuweisungen müssen demselben Benutzer gehören.");
        }

        UserId = userId;
        Assignments = assignments;
        _isSystem = isSystem;
    }

    /// <summary>Nicht angemeldeter Aufrufer. Sieht ausschließlich die öffentliche Ansicht.</summary>
    public static UserPrincipal Anonymous { get; } = new(Guid.Empty, []);

    /// <summary>
    /// Kontext für Hintergrundarbeit ohne angemeldeten Benutzer — Projektionsaufbau,
    /// Migrationen, Seed. Bewusst benannt, damit ein „läuft halt als Admin" im Code
    /// sichtbar ist, statt hinter einem umgangenen Filter zu verschwinden.
    /// </summary>
    public static UserPrincipal System { get; } = new(Guid.Empty, [], isSystem: true);

    public Guid UserId { get; }

    public bool IsAuthenticated => UserId != Guid.Empty;

    public IReadOnlyList<RoleAssignment> Assignments { get; }

    public bool IsSystemAdmin => _isSystem || Assignments.Any(a => a.Role == Role.SystemAdmin);

    /// <summary>
    /// Die Vereine, in denen der Benutzer irgendeine Rolle hat. Grundlage des
    /// Query-Filters — ein SystemAdmin wird dort über <see cref="IsSystemAdmin"/>
    /// behandelt, nicht über diese Liste.
    /// </summary>
    public IReadOnlyCollection<Guid> ClubIds => ScopedTo(ScopeType.Club);

    /// <summary>
    /// Die Turniere, für die der Benutzer eine turniergebundene Rolle hat.
    ///
    /// Ohne diese Liste bliebe ein Turnierleiter oder Schiedsrichter, der keiner
    /// Vereinsrolle hat, für den Query-Filter unsichtbar — sein eigenes Turnier
    /// käme als „nicht gefunden" zurück, obwohl er genau dafür berufen wurde.
    /// </summary>
    public IReadOnlyCollection<Guid> TournamentIds => ScopedTo(ScopeType.Tournament);

    private IReadOnlyCollection<Guid> ScopedTo(ScopeType type) =>
        Assignments
            .Where(a => a.Scope.Type == type && a.Scope.ResourceId is not null)
            .Select(a => a.Scope.ResourceId!.Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// Gewährt einer der angefragten Scopes die Berechtigung?
    ///
    /// Mehrere Scopes, weil dieselbe Handlung auf verschiedenen Wegen erlaubt sein
    /// kann: Ergebnisse eintragen darf sowohl der Schiedsrichter des Turniers als
    /// auch der Administrator des ausrichtenden Vereins. Die Aufrufstelle gibt
    /// beide an, statt die Rangfolge selbst zu kennen.
    /// </summary>
    public bool Can(Permission permission, params ResourceScope[] scopes) =>
        _isSystem || Assignments.Any(a => a.Grants(permission, scopes));

    public void Require(Permission permission, params ResourceScope[] scopes)
    {
        if (!Can(permission, scopes))
        {
            throw new AccessDeniedException(permission, scopes);
        }
    }
}

/// <summary>
/// Fehlende Berechtigung. Wird an der API-Grenze bewusst auf 404 abgebildet und
/// nicht auf 403 — ein 403 verrät, dass die Ressource existiert (ADR-0004).
/// </summary>
public sealed class AccessDeniedException : Exception
{
    public AccessDeniedException(Permission permission, IReadOnlyCollection<ResourceScope> scopes)
        : base($"Keine Berechtigung {permission} in [{string.Join(", ", scopes)}].")
    {
        Permission = permission;
        Scopes = scopes;
    }

    public Permission Permission { get; }

    public IReadOnlyCollection<ResourceScope> Scopes { get; }
}
