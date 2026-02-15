namespace WealthsimpleTradingBot.Models;

public record PortfolioTarget(
    string Symbol,
    string SecurityId,
    decimal TargetWeight,
    decimal TargetValue,
    decimal CurrentValue,
    decimal CurrentWeight,
    decimal DriftPct,
    string Action,
    decimal TradeValue,
    int TradeQuantity
);

public record PortfolioSummary(
    decimal TotalValue,
    decimal CashBalance,
    decimal PositionsValue,
    int NumHoldings,
    List<PortfolioTarget> Targets
);

public record StockScore(
    string Symbol,
    string Name,
    string Sector,
    double MarketCap,
    double AvgVolume,
    double Return90d,
    double Return30d,
    double Volatility,
    double SharpeRatio,
    double CompositeScore,
    bool IsEtf
);
