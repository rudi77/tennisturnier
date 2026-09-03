using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisTurnier.Adapters.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// „Welche Matches hat diese Meldung gespielt" — die häufigste Frage im
    /// ganzen System, und bis hierher ohne Index.
    ///
    /// Von Hand geschrieben, weil EF Core Indizes nicht über die Spalten eines
    /// Komplextyps beschreiben kann: <c>Side1_EntryId</c> und
    /// <c>Side2_EntryId</c> gehören zu <c>MatchSide</c>. Die Spalten sind
    /// Klartext, das SQL ist es damit auch.
    ///
    /// MatchConfiguration hat auf eine Migration dieses Inhalts verwiesen,
    /// solange es sie nicht gab. Der Profil-Lesepfad las deshalb alle Matches
    /// unter dem Query-Filter und suchte im Speicher weiter.
    /// </summary>
    public partial class MeldungsIndizes : Migration
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
