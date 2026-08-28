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

            // Mehr nicht: was der Veranstalter anlegt, führt er als
            // Turnierleiter seines eigenen Turniers — die Rolle bekommt er beim
            // Anlegen. Stünde ManageTournament hier, gälte es global und damit
            // für jedes fremde Turnier.
            [Role.Organizer] = new HashSet<Permission>
            {
                Permission.CreateTournament,
            },

            [Role.TournamentDirector] = new HashSet<Permission>
            {
                Permission.ManageTournament,
                Permission.EnterResults,
                Permission.ViewInternals,
                Permission.ViewMembers,
            },

            [Role.Referee] = new HashSet<Permission>
            {
                Permission.EnterResults,
                Permission.ViewMembers,
            },

            // Ein einziges Recht, und es ist ein Leserecht: das Mitglied sieht,
            // wer sonst dazugehört. Was es darüber hinaus sieht — Spielplan,
            // Draw, Ergebnisse — kommt nicht aus dieser Matrix, sondern aus dem
            // Query-Filter, der an der Rollenzuweisung hängt.
            [Role.Member] = new HashSet<Permission>
            {
                Permission.ViewMembers,
            },
        };

    public static IReadOnlySet<Permission> Of(Role role) =>
        Matrix.TryGetValue(role, out var permissions) ? permissions : None;
}
