namespace WealthsimpleTradingBot.Models;

public enum OrderType { BuyQuantity, SellQuantity }
public enum OrderSubType { Market, Limit }

public record OrderRequest(
    string SecurityId,
    string Symbol,
    int Quantity,
    OrderType Type,
    OrderSubType SubType,
    decimal LimitPrice
)
{
    public Dictionary<string, object> ToApiPayload() => new()
    {
        ["security_id"] = SecurityId,
        ["quantity"] = Quantity,
        ["order_type"] = Type == OrderType.BuyQuantity ? "buy_quantity" : "sell_quantity",
        ["order_sub_type"] = SubType == OrderSubType.Market ? "market" : "limit",
        ["limit_price"] = LimitPrice,
        ["time_in_force"] = "day"
    };
}

public record OrderResponse(
    string OrderId,
    string SecurityId,
    string Symbol,
    int Quantity,
    string OrderType,
    string Status,
    decimal? LimitPrice,
    DateTime? FilledAt,
    DateTime? CreatedAt
);
