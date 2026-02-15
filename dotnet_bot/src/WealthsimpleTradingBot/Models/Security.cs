namespace WealthsimpleTradingBot.Models;

public record SpotQuote(
    decimal Amount,
    decimal? Ask,
    decimal? Bid,
    decimal High,
    decimal Low,
    long Volume,
    string QuoteDate
);

public record Security(
    string Id,
    string Symbol,
    string Name,
    string Exchange,
    string Currency,
    string SecurityType,
    bool IsBuyable,
    SpotQuote? Quote
);
