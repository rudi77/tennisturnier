using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class TournamentProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TournamentProjections",
                columns: table => new
                {
                    TournamentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    ETag = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentProjections", x => x.TournamentId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentProjections");
        }
    }
}
