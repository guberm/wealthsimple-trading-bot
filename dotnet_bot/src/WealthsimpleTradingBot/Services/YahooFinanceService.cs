using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace WealthsimpleTradingBot.Services;

public record HistoricalBar(DateTime Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

public record TickerInfo(double MarketCap, string Sector, string Name);

public interface IYahooFinanceService
{
    Task<Dictionary<string, List<HistoricalBar>>> FetchHistoricalDataAsync(
        List<string> symbols, int lookbackDays);
    Task<TickerInfo> GetTickerInfoAsync(string symbol);
}

/// <summary>
/// Fetches market data from Yahoo Finance using the v8 chart API.
/// No API key needed — uses the public endpoint.
/// </summary>
public class YahooFinanceService : IYahooFinanceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<YahooFinanceService> _logger;

    public YahooFinanceService(HttpClient httpClient, ILogger<YahooFinanceService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
    }

    public async Task<Dictionary<string, List<HistoricalBar>>> FetchHistoricalDataAsync(
        List<string> symbols, int lookbackDays)
    {
        var result = new Dictionary<string, List<HistoricalBar>>();
        var period1 = new DateTimeOffset(DateTime.UtcNow.AddDays(-lookbackDays)).ToUnixTimeSeconds();
        var period2 = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();

        foreach (var symbol in symbols)
        {
            try
            {
                var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}" +
                          $"?period1={period1}&period2={period2}&interval=1d";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Failed to fetch {Symbol}: HTTP {Status}",
                        symbol, (int)response.StatusCode);
                    continue;
                }

                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                var bars = ParseChartResponse(json);
                if (bars.Count > 0)
                    result[symbol] = bars;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error fetching {Symbol}: {Error}", symbol, ex.Message);
            }
        }

        _logger.LogInformation("Fetched historical data for {Count}/{Total} symbols",
            result.Count, symbols.Count);
        return result;
    }

    public async Task<TickerInfo> GetTickerInfoAsync(string symbol)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}" +
                      $"?interval=1d&range=1d";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return new TickerInfo(0, "Unknown", symbol);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var meta = json.GetProperty("chart").GetProperty("result")[0].GetProperty("meta");

            return new TickerInfo(
                MarketCap: 0, // Chart API doesn't return market cap
                Sector: "Unknown",
                Name: meta.TryGetProperty("longName", out var n) ? n.GetString() ?? symbol : symbol
            );
        }
        catch
        {
            return new TickerInfo(0, "Unknown", symbol);
        }
    }

    private static List<HistoricalBar> ParseChartResponse(JsonElement json)
    {
        var bars = new List<HistoricalBar>();
        try
        {
            var result = json.GetProperty("chart").GetProperty("result")[0];
            var timestamps = result.GetProperty("timestamp");
            var indicators = result.GetProperty("indicators").GetProperty("quote")[0];

            var opens = indicators.GetProperty("open");
            var highs = indicators.GetProperty("high");
            var lows = indicators.GetProperty("low");
            var closes = indicators.GetProperty("close");
            var volumes = indicators.GetProperty("volume");

            for (int i = 0; i < timestamps.GetArrayLength(); i++)
            {
                if (closes[i].ValueKind == JsonValueKind.Null) continue;

                bars.Add(new HistoricalBar(
                    Date: DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()).DateTime,
                    Open: GetDecimalSafe(opens[i]),
                    High: GetDecimalSafe(highs[i]),
                    Low: GetDecimalSafe(lows[i]),
                    Close: GetDecimalSafe(closes[i]),
                    Volume: volumes[i].ValueKind != JsonValueKind.Null ? volumes[i].GetInt64() : 0
                ));
            }
        }
        catch { }
        return bars;
    }

    private static decimal GetDecimalSafe(JsonElement el)
        => el.ValueKind != JsonValueKind.Null ? (decimal)el.GetDouble() : 0;
}
