using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RouteWeather.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTypicalClimbHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "TypicalClimbHours",
                table: "Routes",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TypicalClimbHours",
                table: "Routes");
        }
    }
}
