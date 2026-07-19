using System.ComponentModel.DataAnnotations;

namespace RouteWeather.Data.Entities;

public class RouteEntity
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Mountain { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string RouteName { get; set; } = string.Empty;

    public int SummitElevationFt { get; set; }
    public double SummitLat { get; set; }
    public double SummitLon { get; set; }

    [MaxLength(20)]
    public string ClassDifficulty { get; set; } = string.Empty;

    public bool IsGlaciated { get; set; }

    /// <summary>Typical summit-day push in hours (car-to-car or camp-to-camp), slow end of guidebook ranges.</summary>
    public double TypicalClimbHours { get; set; }

    [MaxLength(40)]
    public string SnotelStationTriplet { get; set; } = string.Empty;

    public int RangeId { get; set; }
    public RangeEntity? Range { get; set; }
}
