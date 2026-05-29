using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RouteWeather.API.Services;
using RouteWeather.Data;
using RouteWeather.Data.Repositories;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCors = "FrontendCors";
var frontendOrigin = builder.Configuration["FrontendOrigin"] ?? "http://localhost:4200";

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddOpenApi();

builder.Services.AddCors(opts =>
    opts.AddPolicy(FrontendCors, p =>
        p.WithOrigins(frontendOrigin).AllowAnyHeader().AllowAnyMethod()));

var connectionString = builder.Configuration["CONNECTION_STRING"]
                       ?? builder.Configuration.GetConnectionString("Default")
                       ?? "Data Source=routeweather.db";

builder.Services.AddDbContextFactory<RouteWeatherContext>(opts => opts.UseSqlite(connectionString));

builder.Services.AddScoped<RouteRepository>();
builder.Services.AddScoped<ForecastCacheRepository>();
builder.Services.AddScoped<ConditionsAggregator>();

builder.Services.AddHttpClient<NwsClient>(c =>
{
    c.BaseAddress = new Uri("https://api.weather.gov/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd("BigRouteWeather/0.1 (github.com/pgowdy1/Big_Route_Weather)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/geo+json");
    c.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient<SnotelClient>(c =>
{
    c.BaseAddress = new Uri("https://wcc.sc.egov.usda.gov/awdbRestApi/services/v1/");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    c.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RouteWeatherContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
    await RouteSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(FrontendCors);
app.UseAuthorization();
app.MapControllers();

app.Run();
