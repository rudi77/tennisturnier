using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// Der Bestätigungscode entfällt.
    ///
    /// Er war der Weg eines Melders ohne Konto zurück zu seiner Meldung — acht
    /// Zeichen, die man sich aufschreiben musste. Seit dem Beitritt über ein
    /// Konto ist das Konto dieser Weg, und der Code wäre ein zweiter, den
    /// niemand mehr braucht (ADR-0012).
    ///
    /// Die Spalte fällt mit ihm. Was in ihr stand, war nur für diesen Zweck da.
    /// </summary>
    public partial class BestaetigungscodeEntfaellt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropColumn(
                name: "ConfirmationCode",
                table: "TournamentEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationCode",
                table: "TournamentEntries",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }
    }
}
