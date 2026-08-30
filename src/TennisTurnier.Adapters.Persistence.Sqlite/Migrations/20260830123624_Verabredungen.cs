using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Verabredungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayDates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Discipline = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    VenueName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    StartsAt = table.Column<string>(type: "TEXT", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    IsCancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayDates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayDateInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayDateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Response = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayDateInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayDateInvitations_PlayDates_PlayDateId",
                        column: x => x.PlayDateId,
                        principalTable: "PlayDates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayDateInvitations_PlayDateId_UserId",
                table: "PlayDateInvitations",
                columns: new[] { "PlayDateId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayDateInvitations_UserId",
                table: "PlayDateInvitations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayDates_HostUserId",
                table: "PlayDates",
                column: "HostUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayDates_StartsAt",
                table: "PlayDates",
                column: "StartsAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayDateInvitations");

            migrationBuilder.DropTable(
                name: "PlayDates");
        }
    }
}
