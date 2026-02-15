using Microsoft.Extensions.Options;
using WealthsimpleTradingBot.Api;
using WealthsimpleTradingBot.Configuration;
using WealthsimpleTradingBot.Models;

namespace WealthsimpleTradingBot.Execution;

public interface IOrderExecutor
{
    Task<List<OrderResponse>> ExecuteOrdersAsync(
        List<OrderRequest> sellOrders,
        List<OrderRequest> buyOrders,
        CancellationToken ct = default);
    ExecutionSummary GetExecutionSummary();
}

public record ExecutionSummary(int TotalOrders, int Successful, int Failed, int DailyTradesUsed);

public class OrderExecutor : IOrderExecutor
{
    private readonly IOrderService _orderService;
    private readonly RateLimiter _rateLimiter;
    private readonly SafetySettings _settings;
    private readonly ILogger<OrderExecutor> _logger;
    private readonly List<OrderResponse> _executedOrders = new();
    private int _dailyTradeCount;

    public OrderExecutor(
        IOrderService orderService,
        RateLimiter rateLimiter,
        IOptions<SafetySettings> settings,
        ILogger<OrderExecutor> logger)
    {
        _orderService = orderService;
        _rateLimiter = rateLimiter;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<List<OrderResponse>> ExecuteOrdersAsync(
        List<OrderRequest> sellOrders,
        List<OrderRequest> buyOrders,
        CancellationToken ct = default)
    {
        var results = new List<OrderResponse>();

        // Execute sells first
        _logger.LogInformation("Executing {Count} sell orders...", sellOrders.Count);
        foreach (var order in sellOrders)
        {
            if (_dailyTradeCount >= _settings.MaxDailyTrades)
            {
                _logger.LogWarning("Daily trade limit reached ({Max})", _settings.MaxDailyTrades);
                break;
            }
            if (!ValidateOrder(order)) continue;

            var resp = await ExecuteSingleAsync(order, ct);
            if (resp != null) results.Add(resp);
        }

        // Pause between sells and buys
        if (sellOrders.Count > 0 && buyOrders.Count > 0)
        {
            _logger.LogInformation("Pausing 5s between sells and buys...");
            await Task.Delay(5000, ct);
        }

        // Execute buys
        _logger.LogInformation("Executing {Count} buy orders...", buyOrders.Count);
        foreach (var order in buyOrders)
        {
            if (_dailyTradeCount >= _settings.MaxDailyTrades)
            {
                _logger.LogWarning("Daily trade limit reached ({Max})", _settings.MaxDailyTrades);
                break;
            }
            if (!ValidateOrder(order)) continue;

            var resp = await ExecuteSingleAsync(order, ct);
            if (resp != null) results.Add(resp);
        }

        _executedOrders.AddRange(results);
        _logger.LogInformation("Execution complete: {Executed}/{Total} orders",
            results.Count, sellOrders.Count + buyOrders.Count);
        return results;
    }

    public ExecutionSummary GetExecutionSummary()
    {
        var successful = _executedOrders.Count(o => o.Status != "rejected");
        return new ExecutionSummary(
            _executedOrders.Count, successful,
            _executedOrders.Count - successful, _dailyTradeCount);
    }

    private async Task<OrderResponse?> ExecuteSingleAsync(OrderRequest order, CancellationToken ct)
    {
        try
        {
            await _rateLimiter.AcquireAsync(ct);
            var resp = await _orderService.PlaceOrderAsync(order);
            _dailyTradeCount++;
            _logger.LogInformation("ORDER EXECUTED: {Type} {Qty}x {Symbol} @ ${Price} — status={Status}",
                order.Type, order.Quantity, order.Symbol, order.LimitPrice, resp.Status);
            return resp;
        }
        catch (Exception ex)
        {
            _logger.LogError("Order failed for {Symbol}: {Error}", order.Symbol, ex.Message);
            return null;
        }
    }

    private bool ValidateOrder(OrderRequest order)
    {
        var tradeValue = order.LimitPrice * order.Quantity;
        if (tradeValue > _settings.MaxSingleTradeCad)
        {
            _logger.LogWarning("Order value {Value:C} exceeds max {Max:C} for {Symbol}",
                tradeValue, _settings.MaxSingleTradeCad, order.Symbol);
            return false;
        }
        if (order.Quantity < 1)
        {
            _logger.LogWarning("Invalid quantity {Qty} for {Symbol}", order.Quantity, order.Symbol);
            return false;
        }
        return true;
    }
}
