using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// Die Brücke zwischen Konto und Spieler.
    ///
    /// Nullable, weil die Regel bleibt: wer aus einer hochgeladenen Liste
    /// kommt, hat kein Konto und spielt trotzdem mit. Eindeutig, weil ein
    /// Konto zu genau einem Spieler gehört — mehrere NULL nebeneinander lässt
    /// der Index zu, denn in SQL ist kein NULL wie das andere.
    /// </summary>
    public partial class SpielerKonto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<Guid>(
                name: "UserAccountId",
                table: "Players",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_UserAccountId",
                table: "Players",
                column: "UserAccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropIndex(
                name: "IX_Players_UserAccountId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "UserAccountId",
                table: "Players");
        }
    }
}
