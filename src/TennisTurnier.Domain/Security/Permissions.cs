namespace TennisTurnier.Domain.Security;

/// <summary>
/// Die Rollen-Rechte-Matrix — eine einzige Tabelle, damit die Antwort auf
/// „darf ein Schiedsrichter den Draw ändern?" an genau einer Stelle steht und
/// nicht über Endpunkte verstreut ist.
/// </summary>
public static class Permissions
{
    private static readonly IReadOnlySet<Permission> All = Enum.GetValues<Permission>().ToHashSet();

    private static readonly IReadOnlySet<Permission> None = new HashSet<Permission>();

    private static readonly IReadOnlyDictionary<Role, IReadOnlySet<Permission>> Matrix =
        new Dictionary<Role, IReadOnlySet<Permission>>
        {
            [Role.SystemAdmin] = All,

            [Role.ClubAdmin] = new HashSet<Permission>
            {
                Permission.ManageClub,
                Permission.ManageCourts,
                Permission.ManageTournament,
                Permission.EnterResults,
                Permission.ViewInternals,
            },

            [Role.TournamentDirector] = new HashSet<Permission>
            {
                Permission.ManageTournament,
                Permission.EnterResults,
                Permission.ViewInternals,
            },

            [Role.Referee] = new HashSet<Permission>
            {
                Permission.EnterResults,
            },

            // Ein Spieler verwaltet nur sich selbst. Das läuft nicht über diese
            // Matrix, sondern über den Bezug auf die eigene Benutzer-Id.
            [Role.Player] = None,
        };

    public static IReadOnlySet<Permission> Of(Role role) =>
        Matrix.TryGetValue(role, out var permissions) ? permissions : None;
}
