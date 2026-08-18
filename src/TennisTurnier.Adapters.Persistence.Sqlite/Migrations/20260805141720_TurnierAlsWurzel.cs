using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class TurnierAlsWurzel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormatTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Definition = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormatTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Participants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerIds = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "Tournaments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Discipline = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RegistrationToken = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RegistrationCapacity = table.Column<int>(type: "INTEGER", nullable: true),
                    RegistrationDeadline = table.Column<string>(type: "TEXT", nullable: true),
                    StartsOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SchedulingMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FormatTemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FormatSnapshot = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    VenueAddress = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    VenueCity = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    VenueName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tournaments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Issuer = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Id);
                });

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
                name: "TournamentCourts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TournamentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Surface = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsCenterCourt = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentCourts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentCourts_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TournamentEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TournamentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Seed = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentEntries_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentEntries_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ScopeResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScopeType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleAssignments_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
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
                name: "CourtWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TournamentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourtId = table.Column<Guid>(type: "TEXT", nullable: false),
                    To = table.Column<string>(type: "TEXT", nullable: false),
                    From = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtWindows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtWindows_TournamentCourts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "TournamentCourts",
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
                        name: "FK_CourtAssignments_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourtAssignments_TournamentCourts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "TournamentCourts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "IX_CourtWindows_CourtId",
                table: "CourtWindows",
                column: "CourtId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtWindows_TournamentId_CourtId",
                table: "CourtWindows",
                columns: new[] { "TournamentId", "CourtId" });

            migrationBuilder.CreateIndex(
                name: "IX_FormatTemplates_OwnerUserId",
                table: "FormatTemplates",
                column: "OwnerUserId");

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

            migrationBuilder.CreateIndex(
                name: "IX_Players_LastName_FirstName",
                table: "Players",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_UserId",
                table: "RoleAssignments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentCourts_TournamentId_Name",
                table: "TournamentCourts",
                columns: new[] { "TournamentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntries_ParticipantId",
                table: "TournamentEntries",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntries_TournamentId_ParticipantId",
                table: "TournamentEntries",
                columns: new[] { "TournamentId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_RegistrationToken",
                table: "Tournaments",
                column: "RegistrationToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_StartsOn",
                table: "Tournaments",
                column: "StartsOn");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_Issuer_SubjectId",
                table: "UserAccounts",
                columns: new[] { "Issuer", "SubjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourtAssignments");

            migrationBuilder.DropTable(
                name: "CourtWindows");

            migrationBuilder.DropTable(
                name: "FormatTemplates");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "RoleAssignments");

            migrationBuilder.DropTable(
                name: "TournamentEntries");

            migrationBuilder.DropTable(
                name: "TournamentProjections");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "TournamentCourts");

            migrationBuilder.DropTable(
                name: "UserAccounts");

            migrationBuilder.DropTable(
                name: "Participants");

            migrationBuilder.DropTable(
                name: "Phases");

            migrationBuilder.DropTable(
                name: "Tournaments");
        }
    }
}
