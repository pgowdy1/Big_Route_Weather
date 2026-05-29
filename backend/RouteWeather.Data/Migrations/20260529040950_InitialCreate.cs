using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RouteWeather.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CachedForecasts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RouteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachedForecasts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Routes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Mountain = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RouteName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SummitElevationFt = table.Column<int>(type: "INTEGER", nullable: false),
                    SummitLat = table.Column<double>(type: "REAL", nullable: false),
                    SummitLon = table.Column<double>(type: "REAL", nullable: false),
                    ClassDifficulty = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SnotelStationTriplet = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CachedForecasts_RouteId_Source",
                table: "CachedForecasts",
                columns: new[] { "RouteId", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Routes_Slug",
                table: "Routes",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CachedForecasts");

            migrationBuilder.DropTable(
                name: "Routes");
        }
    }
}
