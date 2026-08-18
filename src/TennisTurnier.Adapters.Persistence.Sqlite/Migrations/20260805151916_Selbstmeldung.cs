using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// Die Selbstmeldung: woher eine Meldung stammt, wann sie einging, und der
    /// Code, mit dem ein Melder ohne Konto zu ihr zurückfindet.
    ///
    /// Die Vorgabewerte gelten nur für Zeilen, die es zum Zeitpunkt der
    /// Migration schon gibt: eine von der Turnierleitung erfasste Meldung ohne
    /// bekannten Zeitpunkt. Neue Zeilen bekommen ihre Werte aus dem Aggregat.
    /// </summary>
    public partial class Selbstmeldung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfirmationCode",
                table: "TournamentEntries",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "TournamentEntries",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Organiser");

            migrationBuilder.AddColumn<string>(
                name: "RegisteredAt",
                table: "TournamentEntries",
                type: "TEXT",
                nullable: false,
                // Das Format des UtcDateTimeOffsetConverter. Eine leere
                // Zeichenkette ließe sich beim Lesen nicht zurückwandeln.
                defaultValue: "2026-01-01T00:00:00.0000000Z");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmationCode",
                table: "TournamentEntries");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "TournamentEntries");

            migrationBuilder.DropColumn(
                name: "RegisteredAt",
                table: "TournamentEntries");
        }
    }
}
