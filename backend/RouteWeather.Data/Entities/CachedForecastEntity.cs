namespace RouteWeather.Data.Entities;

public class CachedForecastEntity
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime FetchedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
