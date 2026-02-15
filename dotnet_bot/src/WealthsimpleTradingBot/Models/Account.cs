namespace WealthsimpleTradingBot.Models;

public record CurrencyAmount(decimal Amount, string Currency);

public record Account(
    string Id,
    string AccountType,
    CurrencyAmount BuyingPower,
    CurrencyAmount CurrentBalance,
    CurrencyAmount NetDeposits,
    string Status
);
