namespace WealthsimpleTradingBot.Models;

public record Position(
    string SecurityId,
    string Symbol,
    decimal Quantity,
    decimal MarketValue,
    decimal BookValue,
    string Currency,
    decimal AveragePrice,
    decimal CurrentPrice,
    decimal GainLoss,
    decimal GainLossPct
);
