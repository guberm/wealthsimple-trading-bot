using Microsoft.Extensions.Options;
using WealthsimpleTradingBot.Configuration;
using WealthsimpleTradingBot.Models;

namespace WealthsimpleTradingBot.Strategy;

public interface IPortfolioRebalancer
{
    PortfolioSummary CalculateTargets(
        List<StockScore> selectedStocks,
        List<Position> currentPositions,
        decimal cashBalance,
        Dictionary<string, decimal> securityPrices,
        Dictionary<string, string> securityIds);

    (List<OrderRequest> SellOrders, List<OrderRequest> BuyOrders) GenerateOrders(
        PortfolioSummary summary);
}

public class PortfolioRebalancer : IPortfolioRebalancer
{
    private readonly RebalancerSettings _rebalancerSettings;
    private readonly SafetySettings _safetySettings;
    private readonly ILogger<PortfolioRebalancer> _logger;

    public PortfolioRebalancer(
        IOptions<RebalancerSettings> rebalancerSettings,
        IOptions<SafetySettings> safetySettings,
        ILogger<PortfolioRebalancer> logger)
    {
        _rebalancerSettings = rebalancerSettings.Value;
        _safetySettings = safetySettings.Value;
        _logger = logger;
    }

    public PortfolioSummary CalculateTargets(
        List<StockScore> selectedStocks,
        List<Position> currentPositions,
        decimal cashBalance,
        Dictionary<string, decimal> securityPrices,
        Dictionary<string, string> securityIds)
    {
        var selectedSymbols = selectedStocks.Select(s => s.Symbol).ToHashSet();
        var positionsValue = currentPositions.Sum(p => p.MarketValue);
        var totalValue = cashBalance + positionsValue;

        if (totalValue <= 0)
        {
            _logger.LogWarning("Total portfolio value is $0 or negative");
            return new PortfolioSummary(totalValue, cashBalance, positionsValue,
                currentPositions.Count, new List<PortfolioTarget>());
        }

        int numBuckets = selectedSymbols.Count;
        decimal targetWeight = 1m / numBuckets;
        decimal targetValuePer = totalValue * targetWeight;
        var posMap = currentPositions.ToDictionary(p => p.Symbol, p => p);
        var targets = new List<PortfolioTarget>();

        foreach (var symbol in selectedSymbols)
        {
            if (!securityPrices.TryGetValue(symbol, out var price) || price <= 0)
            {
                _logger.LogWarning("No price for {Symbol}, skipping", symbol);
                continue;
            }

            var secId = securityIds.GetValueOrDefault(symbol, "");
            posMap.TryGetValue(symbol, out var currentPos);
            var currentValue = currentPos?.MarketValue ?? 0;
            var currentWeight = totalValue > 0 ? currentValue / totalValue : 0;
            var driftPct = targetWeight > 0
                ? Math.Abs(currentWeight - targetWeight) / targetWeight * 100
                : 0;

            var tradeValue = targetValuePer - currentValue;
            var absTrade = Math.Abs(tradeValue);

            string action;
            int tradeQty;

            if (driftPct < _rebalancerSettings.DriftThresholdPct ||
                absTrade < _rebalancerSettings.MinTradeValueCad)
            {
                action = "hold";
                tradeQty = 0;
            }
            else if (tradeValue > 0)
            {
                action = "buy";
                var capped = Math.Min(absTrade, _safetySettings.MaxSingleTradeCad);
                tradeQty = (int)Math.Floor(capped / price);
            }
            else
            {
                action = "sell";
                var capped = Math.Min(absTrade, _safetySettings.MaxSingleTradeCad);
                tradeQty = (int)Math.Floor(capped / price);
            }

            targets.Add(new PortfolioTarget(
                Symbol: symbol,
                SecurityId: secId,
                TargetWeight: targetWeight,
                TargetValue: targetValuePer,
                CurrentValue: currentValue,
                CurrentWeight: currentWeight,
                DriftPct: driftPct,
                Action: action,
                TradeValue: absTrade,
                TradeQuantity: tradeQty
            ));
        }

        // Liquidate positions not in new picks
        foreach (var pos in currentPositions)
        {
            if (!selectedSymbols.Contains(pos.Symbol) && pos.Quantity > 0)
            {
                targets.Add(new PortfolioTarget(
                    Symbol: pos.Symbol,
                    SecurityId: pos.SecurityId,
                    TargetWeight: 0,
                    TargetValue: 0,
                    CurrentValue: pos.MarketValue,
                    CurrentWeight: totalValue > 0 ? pos.MarketValue / totalValue : 0,
                    DriftPct: 100,
                    Action: "sell",
                    TradeValue: pos.MarketValue,
                    TradeQuantity: (int)pos.Quantity
                ));
            }
        }

        _logger.LogInformation(
            "Portfolio: total={Total:C}, cash={Cash:C}, {TargetCount} targets ({Buckets} buckets)",
            totalValue, cashBalance, targets.Count, numBuckets);

        return new PortfolioSummary(totalValue, cashBalance, positionsValue,
            currentPositions.Count, targets);
    }

    public (List<OrderRequest> SellOrders, List<OrderRequest> BuyOrders) GenerateOrders(
        PortfolioSummary summary)
    {
        var sellOrders = new List<OrderRequest>();
        var buyOrders = new List<OrderRequest>();

        foreach (var target in summary.Targets)
        {
            if (target.Action == "hold" || target.TradeQuantity < 1) continue;

            if (target.Action == "sell")
            {
                var price = target.TradeQuantity > 0
                    ? target.CurrentValue / target.TradeQuantity : 0;
                sellOrders.Add(new OrderRequest(
                    SecurityId: target.SecurityId,
                    Symbol: target.Symbol,
                    Quantity: target.TradeQuantity,
                    Type: OrderType.SellQuantity,
                    SubType: OrderSubType.Limit,
                    LimitPrice: price
                ));
            }
            else if (target.Action == "buy")
            {
                var price = target.TradeQuantity > 0
                    ? target.TargetValue / target.TradeQuantity : 0;
                buyOrders.Add(new OrderRequest(
                    SecurityId: target.SecurityId,
                    Symbol: target.Symbol,
                    Quantity: target.TradeQuantity,
                    Type: OrderType.BuyQuantity,
                    SubType: OrderSubType.Limit,
                    LimitPrice: price
                ));
            }
        }

        _logger.LogInformation("Generated {Sells} sell orders, {Buys} buy orders",
            sellOrders.Count, buyOrders.Count);
        return (sellOrders, buyOrders);
    }
}
