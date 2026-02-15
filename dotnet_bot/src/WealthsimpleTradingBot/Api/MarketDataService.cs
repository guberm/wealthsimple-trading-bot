using System.Collections.Concurrent;
using System.Text.Json;
using WealthsimpleTradingBot.Models;

namespace WealthsimpleTradingBot.Api;

public interface IMarketDataService
{
    Task<Security?> SearchSecurityAsync(string symbol);
    Task<decimal> GetQuoteAsync(string symbol);
    Task<string> ResolveSecurityIdAsync(string symbol);
    Task<Dictionary<string, string>> BulkResolveSecuritiesAsync(List<string> symbols);
    Task<Dictionary<string, decimal>> GetBulkQuotesAsync(List<string> symbols);
}

public class MarketDataService : IMarketDataService
{
    private readonly IWealthsimpleClient _client;
    private readonly ConcurrentDictionary<string, Security> _cache = new();
    private readonly ILogger<MarketDataService> _logger;

    public MarketDataService(IWealthsimpleClient client, ILogger<MarketDataService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<Security?> SearchSecurityAsync(string symbol)
    {
        if (_cache.TryGetValue(symbol, out var cached))
            return cached;

        var query = symbol.Replace(".TO", "");
        var data = await _client.GetAsync<JsonElement>("/securities",
            new Dictionary<string, string> { ["query"] = query });

        if (!data.TryGetProperty("results", out var results))
            return null;

        foreach (var raw in results.EnumerateArray())
        {
            var rawSymbol = GetNestedString(raw, "stock", "symbol");
            if (rawSymbol.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                rawSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            {
                var sec = ParseSecurity(raw);
                _cache[symbol] = sec;
                return sec;
            }
        }

        // Take first result as fallback
        if (results.GetArrayLength() > 0)
        {
            var sec = ParseSecurity(results[0]);
            _cache[symbol] = sec;
            return sec;
        }

        _logger.LogWarning("Security not found: {Symbol}", symbol);
        return null;
    }

    public async Task<decimal> GetQuoteAsync(string symbol)
    {
        var sec = await SearchSecurityAsync(symbol);
        return sec?.Quote?.Amount ?? 0;
    }

    public async Task<string> ResolveSecurityIdAsync(string symbol)
    {
        var sec = await SearchSecurityAsync(symbol);
        return sec?.Id ?? throw new InvalidOperationException(
            $"Could not resolve security ID for {symbol}");
    }

    public async Task<Dictionary<string, string>> BulkResolveSecuritiesAsync(List<string> symbols)
    {
        var result = new Dictionary<string, string>();
        foreach (var symbol in symbols)
        {
            try
            {
                result[symbol] = await ResolveSecurityIdAsync(symbol);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not resolve {Symbol}: {Error}", symbol, ex.Message);
            }
        }
        _logger.LogInformation("Resolved {Count}/{Total} securities",
            result.Count, symbols.Count);
        return result;
    }

    public async Task<Dictionary<string, decimal>> GetBulkQuotesAsync(List<string> symbols)
    {
        var result = new Dictionary<string, decimal>();
        foreach (var symbol in symbols)
        {
            var price = await GetQuoteAsync(symbol);
            if (price > 0) result[symbol] = price;
        }
        return result;
    }

    private static Security ParseSecurity(JsonElement raw)
    {
        var stock = raw.TryGetProperty("stock", out var s) ? s : raw;

        SpotQuote? quote = null;
        if (raw.TryGetProperty("quote", out var q))
        {
            quote = new SpotQuote(
                Amount: GetDecimal(q, "amount"),
                Ask: q.TryGetProperty("ask", out var ask) ? ask.GetDecimal() : null,
                Bid: q.TryGetProperty("bid", out var bid) ? bid.GetDecimal() : null,
                High: GetDecimal(q, "high"),
                Low: GetDecimal(q, "low"),
                Volume: q.TryGetProperty("volume", out var vol) ? vol.GetInt64() : 0,
                QuoteDate: GetString(q, "quote_date")
            );
        }

        return new Security(
            Id: GetString(raw, "id"),
            Symbol: GetString(stock, "symbol"),
            Name: GetString(stock, "name"),
            Exchange: GetString(stock, "primary_exchange"),
            Currency: GetString(stock, "currency", "CAD"),
            SecurityType: GetString(stock, "security_type"),
            IsBuyable: stock.TryGetProperty("is_buyable", out var ib) && ib.GetBoolean(),
            Quote: quote
        );
    }

    private static string GetString(JsonElement el, string name, string def = "")
        => el.TryGetProperty(name, out var val) ? val.GetString() ?? def : def;

    private static string GetNestedString(JsonElement el, string parent, string child)
    {
        if (el.TryGetProperty(parent, out var p) && p.TryGetProperty(child, out var c))
            return c.GetString() ?? "";
        return "";
    }

    private static decimal GetDecimal(JsonElement el, string name)
        => el.TryGetProperty(name, out var val) ? val.GetDecimal() : 0;
}
