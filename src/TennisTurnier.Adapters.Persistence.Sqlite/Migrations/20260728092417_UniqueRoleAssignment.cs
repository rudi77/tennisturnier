using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// Von Hand ergänzt. EF Core kann Indizes nicht über die Spalten eines
    /// Komplextyps beschreiben, <c>ResourceScope</c> ist aber genau das — der
    /// Index steht deshalb hier statt in <c>RoleAssignmentConfiguration</c>.
    ///
    /// Er verhindert doppelte Rollenzuweisungen, die sonst entstehen können,
    /// weil <c>UserDirectory.AssignAsync</c> erst liest und dann schreibt.
    /// </summary>
    public partial class UniqueRoleAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_UserId_Role_Scope",
                table: "RoleAssignments",
                columns: ["UserId", "Role", "ScopeType", "ScopeResourceId"],
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoleAssignments_UserId_Role_Scope",
                table: "RoleAssignments");
        }
    }
}
