using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Spielerprofil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "Players",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomeClub",
                table: "Players",
                type: "TEXT",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bio",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "HomeClub",
                table: "Players");
        }
    }
}
