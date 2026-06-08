using System.ComponentModel.DataAnnotations;

namespace RouteWeather.Data.Entities;

public class RangeEntity
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string Slug { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(9)]
    public string Color { get; set; } = string.Empty;

    [Required]
    public string PerimeterGeoJson { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public ICollection<RouteEntity> Routes { get; set; } = new List<RouteEntity>();
}
