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
                Permission.WriteInFeed,
            },

            [Role.Referee] = new HashSet<Permission>
            {
                Permission.EnterResults,
                Permission.ViewMembers,
                Permission.WriteInFeed,
            },

            // Zwei Rechte, und beide betreffen die Gruppe: das Mitglied sieht,
            // wer sonst dazugehört, und darf im Feed schreiben. Was es darüber
            // hinaus sieht — Spielplan, Draw, Ergebnisse — kommt nicht aus
            // dieser Matrix, sondern aus dem Query-Filter, der an der
            // Rollenzuweisung hängt.
            //
            // ADR-0012 stand hier einmal „genau ein Recht". ADR-0014 nimmt das
            // zurück, und zwar an der Stelle, an der die Begründung von damals
            // hinführt: eine Gruppe, in der nur einer reden darf, ist so wenig
            // eine Gruppe wie eine, in der niemand sieht, wer dabei ist.
            [Role.Member] = new HashSet<Permission>
            {
                Permission.ViewMembers,
                Permission.WriteInFeed,
            },
        };

    public static IReadOnlySet<Permission> Of(Role role) =>
        Matrix.TryGetValue(role, out var permissions) ? permissions : None;
}
