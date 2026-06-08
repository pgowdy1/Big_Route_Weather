using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RouteWeather.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyApiCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyApiCalls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DateUtc = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyApiCalls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyApiCalls_DateUtc_Source",
                table: "DailyApiCalls",
                columns: new[] { "DateUtc", "Source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyApiCalls");
        }
    }
}
