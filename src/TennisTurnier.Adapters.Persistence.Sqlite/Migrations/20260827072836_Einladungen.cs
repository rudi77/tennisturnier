using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// Einladungen an Adressen, zu denen es noch kein Konto gibt.
    ///
    /// Ohne Fremdschlüssel — weder auf ein Konto, das es nicht gibt, noch auf
    /// das Turnier: die Tabelle ist wie die Rollenzuweisungen Grundlage der
    /// Sichtbarkeit und darf nicht in deren Query-Filter geraten (ADR-0004).
    ///
    /// Der eindeutige Index über Turnier, Adresse und Rolle macht die zweite
    /// Einladung zum zweiten Klick auf dieselbe Schaltfläche.
    /// </summary>
    public partial class Einladungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TournamentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_Email",
                table: "Invitations",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TournamentId_Email_Role",
                table: "Invitations",
                columns: new[] { "TournamentId", "Email", "Role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropTable(
                name: "Invitations");
        }
    }
}
