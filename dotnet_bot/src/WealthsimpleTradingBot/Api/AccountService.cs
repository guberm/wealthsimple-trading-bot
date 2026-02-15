using System.Text.Json;
using WealthsimpleTradingBot.Models;

namespace WealthsimpleTradingBot.Api;

public interface IAccountService
{
    Task<List<Account>> GetAccountsAsync();
    Task<Account?> GetAccountByTypeAsync(string accountType);
    Task<List<Position>> GetPositionsAsync(string? accountId = null);
    Task<decimal> GetBuyingPowerAsync(string accountId);
    Task<decimal> GetTotalPortfolioValueAsync(string accountId, List<Position>? positions = null);
}

public class AccountService : IAccountService
{
    private readonly IWealthsimpleClient _client;
    private readonly ILogger<AccountService> _logger;

    public AccountService(IWealthsimpleClient client, ILogger<AccountService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<Account>> GetAccountsAsync()
    {
        var data = await _client.GetAsync<JsonElement>("/account/list");
        var accounts = new List<Account>();

        if (data.TryGetProperty("results", out var results))
        {
            foreach (var raw in results.EnumerateArray())
            {
                try
                {
                    accounts.Add(new Account(
                        Id: raw.GetProperty("id").GetString()!,
                        AccountType: GetStringProp(raw, "account_type"),
                        BuyingPower: ParseCurrency(raw, "buying_power"),
                        CurrentBalance: ParseCurrency(raw, "current_balance"),
                        NetDeposits: ParseCurrency(raw, "net_deposits"),
                        Status: GetStringProp(raw, "status")
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to parse account: {Error}", ex.Message);
                }
            }
        }

        _logger.LogInformation("Found {Count} accounts", accounts.Count);
        return accounts;
    }

    public async Task<Account?> GetAccountByTypeAsync(string accountType)
    {
        var accounts = await GetAccountsAsync();
        var account = accounts.FirstOrDefault(a => a.AccountType == accountType);
        if (account == null)
            _logger.LogWarning("No account found with type: {Type}", accountType);
        return account;
    }

    public async Task<List<Position>> GetPositionsAsync(string? accountId = null)
    {
        var queryParams = accountId != null
            ? new Dictionary<string, string> { ["account_id"] = accountId }
            : null;

        var data = await _client.GetAsync<JsonElement>("/account/positions", queryParams);
        var positions = new List<Position>();

        if (data.TryGetProperty("results", out var results))
        {
            foreach (var raw in results.EnumerateArray())
            {
                try
                {
                    var quantity = GetDecimalProp(raw, "quantity");
                    var currentPrice = GetNestedDecimal(raw, "quote", "amount");
                    var marketValue = quantity * currentPrice;
                    var bookValue = GetNestedDecimal(raw, "book_value", "amount");

                    positions.Add(new Position(
                        SecurityId: GetStringProp(raw, "id"),
                        Symbol: GetNestedString(raw, "stock", "symbol"),
                        Quantity: quantity,
                        MarketValue: marketValue,
                        BookValue: bookValue,
                        Currency: GetNestedString(raw, "stock", "currency"),
                        AveragePrice: GetNestedDecimal(raw, "entry_price", "amount"),
                        CurrentPrice: currentPrice,
                        GainLoss: marketValue - bookValue,
                        GainLossPct: bookValue > 0 ? (marketValue - bookValue) / bookValue * 100 : 0
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to parse position: {Error}", ex.Message);
                }
            }
        }

        _logger.LogInformation("Found {Count} positions", positions.Count);
        return positions;
    }

    public async Task<decimal> GetBuyingPowerAsync(string accountId)
    {
        var accounts = await GetAccountsAsync();
        return accounts.FirstOrDefault(a => a.Id == accountId)?.BuyingPower.Amount ?? 0;
    }

    public async Task<decimal> GetTotalPortfolioValueAsync(
        string accountId, List<Position>? positions = null)
    {
        var cash = await GetBuyingPowerAsync(accountId);
        positions ??= await GetPositionsAsync(accountId);
        var positionsValue = positions.Sum(p => p.MarketValue);
        var total = cash + positionsValue;
        _logger.LogInformation(
            "Portfolio: cash={Cash:C}, positions={Positions:C}, total={Total:C}",
            cash, positionsValue, total);
        return total;
    }

    private static CurrencyAmount ParseCurrency(JsonElement parent, string propName)
    {
        if (parent.TryGetProperty(propName, out var curr))
        {
            return new CurrencyAmount(
                Amount: curr.TryGetProperty("amount", out var amt) ? amt.GetDecimal() : 0,
                Currency: curr.TryGetProperty("currency", out var c) ? c.GetString()! : "CAD"
            );
        }
        return new CurrencyAmount(0, "CAD");
    }

    private static string GetStringProp(JsonElement el, string name)
        => el.TryGetProperty(name, out var val) ? val.GetString() ?? "" : "";

    private static decimal GetDecimalProp(JsonElement el, string name)
        => el.TryGetProperty(name, out var val) ? val.GetDecimal() : 0;

    private static string GetNestedString(JsonElement el, string parent, string child)
    {
        if (el.TryGetProperty(parent, out var p) && p.TryGetProperty(child, out var c))
            return c.GetString() ?? "";
        return "";
    }

    private static decimal GetNestedDecimal(JsonElement el, string parent, string child)
    {
        if (el.TryGetProperty(parent, out var p) && p.TryGetProperty(child, out var c))
            return c.GetDecimal();
        return 0;
    }
}
