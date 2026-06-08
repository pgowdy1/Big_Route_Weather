using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RouteWeather.Data.Migrations
{
    public partial class UpdateRangePolygons : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpdatePolygon(migrationBuilder, "cascades",       "{\"type\":\"Polygon\",\"coordinates\":[[[-122.5,49.1],[-120.8,49.1],[-120.8,41.1],[-122.5,41.1],[-122.5,49.1]]]}");
            UpdatePolygon(migrationBuilder, "sierra-nevada",  "{\"type\":\"Polygon\",\"coordinates\":[[[-118.8,37.4],[-117.9,37.4],[-117.9,36.2],[-118.8,36.2],[-118.8,37.4]]]}");
            UpdatePolygon(migrationBuilder, "wind-river",     "{\"type\":\"Polygon\",\"coordinates\":[[[-110.0,43.5],[-108.8,43.5],[-108.8,42.4],[-110.0,42.4],[-110.0,43.5]]]}");
            UpdatePolygon(migrationBuilder, "sawtooth",       "{\"type\":\"Polygon\",\"coordinates\":[[[-115.2,44.3],[-114.7,44.3],[-114.7,43.8],[-115.2,43.8],[-115.2,44.3]]]}");
            UpdatePolygon(migrationBuilder, "wasatch",        "{\"type\":\"Polygon\",\"coordinates\":[[[-112.0,40.9],[-111.4,40.9],[-111.4,39.6],[-112.0,39.6],[-112.0,40.9]]]}");
            UpdatePolygon(migrationBuilder, "colorado-14ers", "{\"type\":\"Polygon\",\"coordinates\":[[[-108.3,40.5],[-104.8,40.5],[-104.8,36.9],[-108.3,36.9],[-108.3,40.5]]]}");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: we don't preserve the pre-fix polygons. If someone needs to roll back,
            // they can do so manually from git history (commit 47dc0a1).
        }

        private static void UpdatePolygon(MigrationBuilder mb, string slug, string geoJson)
        {
            // SQLite single-quote escape: double them in the literal.
            var escaped = geoJson.Replace("'", "''");
            mb.Sql($"UPDATE Ranges SET PerimeterGeoJson = '{escaped}' WHERE Slug = '{slug}';");
        }
    }
}
