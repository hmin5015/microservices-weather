using CloudWeather.Report.Config;
using CloudWeather.Report.DataAccess;
using CloudWeather.Report.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CloudWeather.Report.BusinessLogic;

public interface IWeatherReportAggregator
{
    /// <summary>
    /// Builds and returns a Weather Report.
    /// Persists WeatherReport data
    /// </summary>
    /// <param name="zip"></param>
    /// <param name="days"></param>
    /// <returns></returns>
    Task<WeatherReport> BuildReport(string zip, int days);
}

public class WeatherReportAggregator : IWeatherReportAggregator
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<WeatherReportAggregator> _logger;
    private readonly WeatherDataConfig _weatherDataConfig;
    private readonly WeatherReportDbContext _db;

    public WeatherReportAggregator(
      IHttpClientFactory http,
      ILogger<WeatherReportAggregator> logger,
      IOptions<WeatherDataConfig> weatherConfig,
      WeatherReportDbContext db
    ) {
        _http = http;
        _logger = logger;
        _weatherDataConfig = weatherConfig.Value;
        _db = db;
    }

    public async Task<WeatherReport> BuildReport(string zip, int days)
    {
        var httpClient = _http.CreateClient();

        var precipData = await FetchPrecipitationData(httpClient, zip, days);
        var (totalSnow, totalRain) = GetTotalAmountInches(precipData);

        // save log for total snow/rain inches by zip/days
        _logger.LogInformation(
            $"zip: {zip} over last {days} days: " +
            $"total snow: {totalSnow}, rain: {totalRain}");

        var tempData = await FetchTemperatureData(httpClient, zip, days);
        var averageHighTemp = tempData.Average(t => t.TempHighF);
        var averageLowTemp = tempData.Average(t => t.TempLowF);

        // save log for total snow/rain inches by zip/days
        _logger.LogInformation(
            $"zip: {zip} over last {days} days: " +
            $"average low temp: {averageLowTemp}, average high temp: {averageHighTemp}");

        var weatherReport = new WeatherReport
        {
            AverageHighF = Math.Round(averageHighTemp, 1),
            AverageLowF = Math.Round(averageLowTemp, 1),
            RainfallTotalInches = totalRain,
            SnowTotalInches = totalSnow,
            ZipCode = zip,
            CreatedOn = DateTime.UtcNow
        };

        // TODO: Use 'cached' weather reports insted of making round trips when possible?
        _db.Add(weatherReport);
        await _db.SaveChangesAsync();

        return weatherReport;
    }

    private static (decimal TotalSnowInches, decimal totalRainInches) GetTotalAmountInches(IEnumerable<PrecipitationModel> precipData)
    {
        var totalSnowInches = precipData
            .Where(p => p.WeatherType == "snow")
            .Sum(p => p.AmountInches);

        var totalrainInches = precipData
            .Where(p => p.WeatherType == "rain")
            .Sum(p => p.AmountInches);

        return (Math.Round(totalSnowInches, 1), Math.Round(totalrainInches, 1));
    }

    private async Task<List<TemperatureModel>> FetchTemperatureData(HttpClient httpClient, string zip, int days)
    {
        var endpoint = BuildTemperatureServiceEndpoint(zip, days);
        var temperatureRecords = await httpClient.GetAsync(endpoint);
        var jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var temperatureData = await temperatureRecords
            .Content
            .ReadFromJsonAsync<List<TemperatureModel>>(jsonSerializerOptions);
        return temperatureData ?? new List<TemperatureModel>();
    }

    private async Task<List<PrecipitationModel>> FetchPrecipitationData(HttpClient httpClient, string zip, int days)
    {
        var endpoint = BuildPrecipitationServiceEndpoint(zip, days);
        var precipRecords = await httpClient.GetAsync(endpoint);
        var jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var precipData = await precipRecords
            .Content
            .ReadFromJsonAsync<List<PrecipitationModel>>(jsonSerializerOptions);
        return precipData ?? new List<PrecipitationModel>();
    }

    private string BuildTemperatureServiceEndpoint(string zip, int days)
    {
        var tempServiceProtocol = _weatherDataConfig.TempDataProtocol;
        var tempServiceHost = _weatherDataConfig.TempDataHost;
        var tempServicePort = _weatherDataConfig.TempDataPort;
        return $"{tempServiceProtocol}://{tempServiceHost}:{tempServicePort}/observation/{zip}?days={days}";
    }

    private string BuildPrecipitationServiceEndpoint(string zip, int days)
    {
        var precipServiceProtocol = _weatherDataConfig.PrecipDataProtocol;
        var precipServiceHost = _weatherDataConfig.PrecipDataHost;
        var precipServicePort = _weatherDataConfig.PrecipDataPort;
        return $"{precipServiceProtocol}://{precipServiceHost}:{precipServicePort}/observation/{zip}?days={days}";
    }
}
