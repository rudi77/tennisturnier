using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// Von Hand ergänzt. EF Core kann Indizes nicht über die Spalten eines
    /// Komplextyps beschreiben, und <c>MatchSide</c> ist genau das — der Index
    /// steht deshalb hier statt in <c>MatchConfiguration</c>.
    ///
    /// „Welche Matches hat dieser Teilnehmer" ist die häufigste Frage im ganzen
    /// System: sie steckt in jeder Spielplanprüfung, in der Ergebniseingabe und
    /// in der öffentlichen Ansicht. Ohne Index wäre das je Abfrage ein
    /// Tabellendurchlauf.
    /// </summary>
    public partial class MatchSideEntryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Matches_Side1_EntryId",
                table: "Matches",
                column: "Side1_EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Side2_EntryId",
                table: "Matches",
                column: "Side2_EntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Matches_Side1_EntryId", table: "Matches");
            migrationBuilder.DropIndex(name: "IX_Matches_Side2_EntryId", table: "Matches");
        }
    }
}
