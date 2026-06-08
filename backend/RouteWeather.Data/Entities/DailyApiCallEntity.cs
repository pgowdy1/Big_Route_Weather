namespace RouteWeather.Data.Entities;

public class DailyApiCallEntity
{
    public int Id { get; set; }
    public DateOnly DateUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
