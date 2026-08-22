using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// Dieselbe Rolle im selben Scope gibt es je Benutzer genau einmal.
    ///
    /// Die Vorabprüfung in <c>UserDirectory.AssignAsync</c> allein genügt nicht:
    /// zwischen Lesen und Schreiben passt eine zweite Vergabe, und zwei gleiche
    /// Zuweisungen sind nicht bloß unschön — beim Entziehen bliebe die zweite
    /// stehen, und wer die Rolle verlieren sollte, behielte sie.
    ///
    /// Als Rohbefehl und nicht über den Modellbau: EF Core kann keinen Index über
    /// die Spalten eines Komplextyps beschreiben, und <c>Scope</c> ist einer.
    /// <c>COALESCE</c> steht dabei für den globalen Scope, dessen Ressource leer
    /// ist — ohne ihn zählte SQLite jedes NULL als eigenen Wert, und ausgerechnet
    /// die Rollen ohne Turnier blieben ungeschützt.
    /// </summary>
    public partial class UniqueRoleAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Was vor dieser Migration doppelt entstanden ist, bleibt einmal
            // stehen. Ohne diesen Schritt ließe sich der Index auf einer
            // bestehenden Datenbank gar nicht anlegen.
            migrationBuilder.Sql(
                """
                DELETE FROM RoleAssignments
                WHERE rowid NOT IN (
                    SELECT MIN(rowid) FROM RoleAssignments
                    GROUP BY UserId, Role, ScopeType, COALESCE(ScopeResourceId, '')
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IX_RoleAssignments_UserId_Role_Scope
                ON RoleAssignments (UserId, Role, ScopeType, COALESCE(ScopeResourceId, ''));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("DROP INDEX IX_RoleAssignments_UserId_Role_Scope;");
        }
    }
}
