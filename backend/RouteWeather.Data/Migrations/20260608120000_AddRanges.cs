using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RouteWeather.Data.Migrations
{
    public partial class AddRanges : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ranges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                    PerimeterGeoJson = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_Ranges", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_Ranges_Slug",
                table: "Ranges",
                column: "Slug",
                unique: true);

            migrationBuilder.AddColumn<int>(
                name: "RangeId",
                table: "Routes",
                type: "INTEGER",
                nullable: true);

            // Seed the colorado-14ers range FIRST so the backfill UPDATE below can find it.
            migrationBuilder.Sql(@"
                INSERT INTO Ranges (Slug, Name, Color, PerimeterGeoJson, Description, DisplayOrder)
                VALUES ('colorado-14ers', 'Colorado 14ers', '#e8b04f',
                        '{""type"":""Polygon"",""coordinates"":[[[-108.0,40.5],[-105.4,40.5],[-105.0,39.8],[-105.0,38.4],[-105.5,37.0],[-107.0,37.0],[-108.2,37.5],[-108.5,38.5],[-108.3,39.5],[-108.0,40.5]]]}',
                        'The 58 peaks above 14,000 ft in Colorado.',
                        6);");

            // Backfill: every existing route is a Colorado 14er.
            migrationBuilder.Sql(@"
                UPDATE Routes
                SET RangeId = (SELECT Id FROM Ranges WHERE Slug = 'colorado-14ers');");

            // Now make RangeId non-nullable + add the FK.
            migrationBuilder.AlterColumn<int>(
                name: "RangeId",
                table: "Routes",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Routes_Ranges_RangeId",
                table: "Routes",
                column: "RangeId",
                principalTable: "Ranges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(
                name: "IX_Routes_RangeId",
                table: "Routes",
                column: "RangeId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Routes_Ranges_RangeId",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Routes_RangeId",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "RangeId",
                table: "Routes");

            migrationBuilder.DropTable(
                name: "Ranges");
        }
    }
}
