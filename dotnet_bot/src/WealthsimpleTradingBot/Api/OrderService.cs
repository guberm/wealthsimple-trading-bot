using System.Text.Json;
using WealthsimpleTradingBot.Models;

namespace WealthsimpleTradingBot.Api;

public interface IOrderService
{
    Task<OrderResponse> PlaceOrderAsync(OrderRequest order);
    Task<List<OrderResponse>> GetOrdersAsync();
    Task CancelOrderAsync(string orderId);
    Task<List<OrderResponse>> GetPendingOrdersAsync();
}

public class OrderService : IOrderService
{
    private readonly IWealthsimpleClient _client;
    private readonly ILogger<OrderService> _logger;

    public OrderService(IWealthsimpleClient client, ILogger<OrderService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<OrderResponse> PlaceOrderAsync(OrderRequest order)
    {
        _logger.LogInformation("Placing order: {Type} {Qty}x {Symbol} @ ${Price}",
            order.Type, order.Quantity, order.Symbol, order.LimitPrice);

        var data = await _client.PostAsync<JsonElement>("/orders", order.ToApiPayload());
        return ParseOrder(data);
    }

    public async Task<List<OrderResponse>> GetOrdersAsync()
    {
        var data = await _client.GetAsync<JsonElement>("/orders");
        var orders = new List<OrderResponse>();

        if (data.TryGetProperty("results", out var results))
        {
            foreach (var raw in results.EnumerateArray())
                orders.Add(ParseOrder(raw));
        }

        return orders;
    }

    public async Task CancelOrderAsync(string orderId)
    {
        _logger.LogInformation("Cancelling order: {OrderId}", orderId);
        await _client.DeleteAsync($"/orders/{orderId}");
    }

    public async Task<List<OrderResponse>> GetPendingOrdersAsync()
    {
        var orders = await GetOrdersAsync();
        var pending = orders.Where(o =>
            o.Status is "submitted" or "pending" or "new").ToList();
        _logger.LogInformation("Found {Count} pending orders", pending.Count);
        return pending;
    }

    private static OrderResponse ParseOrder(JsonElement raw)
    {
        decimal? limitPrice = null;
        if (raw.TryGetProperty("limit_price", out var lp))
        {
            if (lp.ValueKind == JsonValueKind.Object &&
                lp.TryGetProperty("amount", out var lpAmount))
                limitPrice = lpAmount.GetDecimal();
            else if (lp.ValueKind == JsonValueKind.Number)
                limitPrice = lp.GetDecimal();
        }

        return new OrderResponse(
            OrderId: GetStr(raw, "order_id", GetStr(raw, "id")),
            SecurityId: GetStr(raw, "security_id"),
            Symbol: GetStr(raw, "symbol"),
            Quantity: raw.TryGetProperty("quantity", out var q) ? q.GetInt32() : 0,
            OrderType: GetStr(raw, "order_type"),
            Status: GetStr(raw, "status"),
            LimitPrice: limitPrice,
            FilledAt: raw.TryGetProperty("filled_at", out var fa) && fa.ValueKind != JsonValueKind.Null
                ? fa.GetDateTime() : null,
            CreatedAt: raw.TryGetProperty("created_at", out var ca) && ca.ValueKind != JsonValueKind.Null
                ? ca.GetDateTime() : null
        );
    }

    private static string GetStr(JsonElement el, string name, string def = "")
        => el.TryGetProperty(name, out var val) ? val.GetString() ?? def : def;
}
