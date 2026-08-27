using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// Privat als Voreinstellung.
    ///
    /// Der Vorgabewert false gilt auch für alles, was es schon gibt: bestehende
    /// Turniere waren bis hierher für jeden mit der Kennung sichtbar und sind
    /// es nach dieser Migration nicht mehr. Das ist die Absicht — wer sie
    /// wieder öffnen will, tut es mit einem Schalter, und die andere Richtung
    /// wäre die, die man nicht rückgängig machen kann (ADR-0012).
    /// </summary>
    public partial class TurnierSichtbarkeit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Tournaments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Tournaments");
        }
    }
}
