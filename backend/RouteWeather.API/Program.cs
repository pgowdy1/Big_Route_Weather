using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RouteWeather.API.Options;
using RouteWeather.API.Services;
using RouteWeather.Core.Grading;
using RouteWeather.Core.Sources;
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

builder.Services.AddMemoryCache();
builder.Services.AddScoped<RouteRepository>();
builder.Services.AddScoped<ForecastCacheRepository>();
builder.Services.AddScoped<ConditionsAggregator>();

builder.Services.Configure<ForecastSourcesOptions>(builder.Configuration.GetSection("ForecastSources"));
builder.Services.AddSingleton<DailyCallCounter>();
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<ForecastSourcesOptions>>().Value.ConsensusThresholds;
    return new ConsensusCalculator(opts.HighMaxCv, opts.MediumMaxCv);
});

var forecastConfig = new ForecastSourcesOptions();
builder.Configuration.GetSection("ForecastSources").Bind(forecastConfig);

builder.Services.AddHttpClient<NwsClient>(c =>
{
    c.BaseAddress = new Uri("https://api.weather.gov/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd("BigRouteWeather/0.1 (github.com/pgowdy1/Big_Route_Weather)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/geo+json");
    c.Timeout = TimeSpan.FromSeconds(15);
});
if (forecastConfig.IsEnabled("NWS"))
{
    builder.Services.AddScoped<IForecastSource>(sp => sp.GetRequiredService<NwsClient>());
}

builder.Services.AddHttpClient<SnotelClient>(c =>
{
    c.BaseAddress = new Uri("https://wcc.sc.egov.usda.gov/awdbRestApi/services/v1/");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    c.Timeout = TimeSpan.FromSeconds(15);
});
if (forecastConfig.IsEnabled("SNOTEL"))
{
    builder.Services.AddScoped<ISnowpackSource>(sp => sp.GetRequiredService<SnotelClient>());
}

builder.Services.AddHttpClient<OpenMeteoClient>(c =>
{
    c.BaseAddress = new Uri("https://api.open-meteo.com/");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    c.Timeout = TimeSpan.FromSeconds(15);
});
RegisterOpenMeteoSource<OpenMeteoGfsSource>(builder.Services,   "OpenMeteo-GFS",   forecastConfig);
RegisterOpenMeteoSource<OpenMeteoEcmwfSource>(builder.Services, "OpenMeteo-ECMWF", forecastConfig);
RegisterOpenMeteoSource<OpenMeteoIconSource>(builder.Services,  "OpenMeteo-ICON",  forecastConfig);
RegisterOpenMeteoSource<OpenMeteoHrrrSource>(builder.Services,  "OpenMeteo-HRRR",  forecastConfig);

static void RegisterOpenMeteoSource<T>(IServiceCollection services, string name, ForecastSourcesOptions cfg)
    where T : OpenMeteoModelSource
{
    if (!cfg.IsEnabled(name)) return;
    services.AddScoped<T>();
    services.AddScoped<IForecastSource>(sp => sp.GetRequiredService<T>());
}

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
