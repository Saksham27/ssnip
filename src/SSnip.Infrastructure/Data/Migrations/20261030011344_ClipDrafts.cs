using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SSnip.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClipDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClipDrafts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    InMs = table.Column<long>(type: "INTEGER", nullable: false),
                    OutMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClipDrafts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClipDrafts_Source",
                table: "ClipDrafts",
                column: "Source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClipDrafts");
        }
    }
}
