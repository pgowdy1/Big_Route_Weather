namespace RouteWeather.Core.Models;

public record Route(
    string Slug,
    string Mountain,
    string RouteName,
    int SummitElevationFt,
    double SummitLat,
    double SummitLon,
    string ClassDifficulty,
    string SnotelStationTriplet,
    double TypicalClimbHours = 0
);
