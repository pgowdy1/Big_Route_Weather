using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RouteWeather.Data.Migrations
{
    public partial class ExpandCascadeSierraPolygons : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpdatePolygon(migrationBuilder, "cascades",      "{\"type\":\"Polygon\",\"coordinates\":[[[-122.7,49.1],[-120.3,49.1],[-120.3,40.2],[-122.7,40.2],[-122.7,49.1]]]}");
            UpdatePolygon(migrationBuilder, "sierra-nevada", "{\"type\":\"Polygon\",\"coordinates\":[[[-119.6,38.3],[-117.9,38.3],[-117.9,36.1],[-119.6,36.1],[-119.6,38.3]]]}");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            UpdatePolygon(migrationBuilder, "cascades",      "{\"type\":\"Polygon\",\"coordinates\":[[[-122.5,49.1],[-120.8,49.1],[-120.8,41.1],[-122.5,41.1],[-122.5,49.1]]]}");
            UpdatePolygon(migrationBuilder, "sierra-nevada", "{\"type\":\"Polygon\",\"coordinates\":[[[-118.8,37.4],[-117.9,37.4],[-117.9,36.2],[-118.8,36.2],[-118.8,37.4]]]}");
        }

        private static void UpdatePolygon(MigrationBuilder mb, string slug, string geoJson)
        {
            // SQLite single-quote escape: double them in the literal.
            var escaped = geoJson.Replace("'", "''");
            mb.Sql($"UPDATE Ranges SET PerimeterGeoJson = '{escaped}' WHERE Slug = '{slug}';");
        }
    }
}
