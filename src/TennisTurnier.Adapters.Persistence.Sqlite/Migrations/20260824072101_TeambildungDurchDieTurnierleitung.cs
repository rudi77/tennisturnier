using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// Ein Doppel, dessen Teams die Turnierleitung bildet.
    ///
    /// Zwei Spalten: woher die Paare eines Turniers kommen, und zu welchem Team
    /// eine Einzelmeldung gehört. Bestehende Turniere bekommen „Registered" —
    /// bei ihnen haben sich die Paare selbst gemeldet, und das war bis hierher
    /// der einzige Weg.
    /// </summary>
    public partial class TeambildungDurchDieTurnierleitung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<string>(
                name: "TeamFormation",
                table: "Tournaments",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Registered");

            migrationBuilder.AddColumn<Guid>(
                name: "TeamEntryId",
                table: "TournamentEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntries_TeamEntryId",
                table: "TournamentEntries",
                column: "TeamEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropIndex(
                name: "IX_TournamentEntries_TeamEntryId",
                table: "TournamentEntries");

            migrationBuilder.DropColumn(
                name: "TeamFormation",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "TeamEntryId",
                table: "TournamentEntries");
        }
    }
}
