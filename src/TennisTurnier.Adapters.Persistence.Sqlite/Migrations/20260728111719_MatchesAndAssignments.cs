using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class MatchesAndAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Phases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TournamentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Phases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Phases_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TournamentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PhaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Group = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Score = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Side1_EntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Side1_Origin = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Side2_EntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Side2_Origin = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Matches_Phases_PhaseId",
                        column: x => x.PhaseId,
                        principalTable: "Phases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourtAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TournamentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourtId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceOnCourt = table.Column<int>(type: "INTEGER", nullable: false),
                    EarliestStart = table.Column<string>(type: "TEXT", nullable: true),
                    PlannedStart = table.Column<string>(type: "TEXT", nullable: true),
                    EstimatedDuration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    ActualStart = table.Column<string>(type: "TEXT", nullable: true),
                    ActualEnd = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtAssignments_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtAssignments_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourtAssignments_CourtId_SequenceOnCourt",
                table: "CourtAssignments",
                columns: new[] { "CourtId", "SequenceOnCourt" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtAssignments_MatchId",
                table: "CourtAssignments",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtAssignments_TournamentId",
                table: "CourtAssignments",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_PhaseId_Round_Position",
                table: "Matches",
                columns: new[] { "PhaseId", "Round", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_TournamentId",
                table: "Matches",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_Phases_TournamentId_Ordinal",
                table: "Phases",
                columns: new[] { "TournamentId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourtAssignments");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "Phases");
        }
    }
}
