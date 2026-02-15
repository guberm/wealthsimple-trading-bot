using WealthsimpleTradingBot.Models;

namespace WealthsimpleTradingBot.Execution;

public record SimulatedOrder(
    string Action, string Symbol, int Quantity,
    decimal Price, decimal TotalValue, DateTime Timestamp);

public class DryRunSimulator
{
    private readonly ILogger<DryRunSimulator> _logger;
    private readonly List<SimulatedOrder> _simulatedOrders = new();
    private decimal _simulatedCash;

    private const string Banner = @"
╔══════════════════════════════════════════════════════════════╗
║        *** DRY RUN — NO REAL TRADES EXECUTED ***            ║
╚══════════════════════════════════════════════════════════════╝";

    public DryRunSimulator(ILogger<DryRunSimulator> logger)
    {
        _logger = logger;
    }

    public List<SimulatedOrder> SimulateOrders(
        List<OrderRequest> sellOrders,
        List<OrderRequest> buyOrders,
        decimal currentCash,
        Dictionary<string, decimal> prices)
    {
        _simulatedCash = currentCash;
        _simulatedOrders.Clear();

        _logger.LogInformation(Banner);

        foreach (var order in sellOrders)
        {
            var price = prices.GetValueOrDefault(order.Symbol, order.LimitPrice);
            var value = price * order.Quantity;
            _simulatedCash += value;

            var sim = new SimulatedOrder("SELL", order.Symbol, order.Quantity,
                price, value, DateTime.Now);
            _simulatedOrders.Add(sim);
            _logger.LogInformation("[DRY RUN] Would SELL {Qty} shares of {Symbol} @ {Price:C} = {Value:C}",
                order.Quantity, order.Symbol, price, value);
        }

        foreach (var order in buyOrders)
        {
            var price = prices.GetValueOrDefault(order.Symbol, order.LimitPrice);
            var value = price * order.Quantity;

            if (value > _simulatedCash)
            {
                _logger.LogWarning("[DRY RUN] SKIP BUY {Symbol}: need {Need:C} but only {Have:C} cash",
                    order.Symbol, value, _simulatedCash);
                continue;
            }

            _simulatedCash -= value;
            var sim = new SimulatedOrder("BUY", order.Symbol, order.Quantity,
                price, value, DateTime.Now);
            _simulatedOrders.Add(sim);
            _logger.LogInformation("[DRY RUN] Would BUY {Qty} shares of {Symbol} @ {Price:C} = {Value:C}",
                order.Quantity, order.Symbol, price, value);
        }

        return _simulatedOrders;
    }

    public void PrintSimulationReport(PortfolioSummary summary)
    {
        Console.WriteLine(Banner);
        Console.WriteLine(new string('=', 60));
        Console.WriteLine("  Portfolio Summary");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"  Total Value:     {summary.TotalValue,12:C}");
        Console.WriteLine($"  Cash Balance:    {summary.CashBalance,12:C}");
        Console.WriteLine($"  Positions Value: {summary.PositionsValue,12:C}");
        Console.WriteLine($"  Holdings:        {summary.NumHoldings,12}");
        Console.WriteLine();

        Console.WriteLine($"  {"Symbol",-12} {"Target%",8} {"Current%",9} {"Drift%",7} {"Action",6} {"Qty",5} {"Value",10}");
        Console.WriteLine($"  {new string('-', 12)} {new string('-', 8)} {new string('-', 9)} {new string('-', 7)} {new string('-', 6)} {new string('-', 5)} {new string('-', 10)}");

        foreach (var t in summary.Targets)
        {
            Console.WriteLine(
                $"  {t.Symbol,-12} {t.TargetWeight * 100,7:F1}% {t.CurrentWeight * 100,8:F1}% " +
                $"{t.DriftPct,6:F1}% {t.Action,6} {t.TradeQuantity,5} {t.TradeValue,10:C}");
        }

        Console.WriteLine();
        if (_simulatedOrders.Count > 0)
        {
            Console.WriteLine("  Simulated Trades:");
            Console.WriteLine($"  {"Action",-6} {"Symbol",-12} {"Qty",5} {"Price",10} {"Value",12}");
            Console.WriteLine($"  {new string('-', 6)} {new string('-', 12)} {new string('-', 5)} {new string('-', 10)} {new string('-', 12)}");

            decimal totalBought = 0, totalSold = 0;
            foreach (var o in _simulatedOrders)
            {
                Console.WriteLine($"  {o.Action,-6} {o.Symbol,-12} {o.Quantity,5} {o.Price,10:C} {o.TotalValue,12:C}");
                if (o.Action == "BUY") totalBought += o.TotalValue;
                else totalSold += o.TotalValue;
            }

            Console.WriteLine($"\n  Total Sold:    {totalSold,12:C}");
            Console.WriteLine($"  Total Bought:  {totalBought,12:C}");
            Console.WriteLine($"  Cash After:    {_simulatedCash,12:C}");
        }
        else
        {
            Console.WriteLine("  No trades needed — portfolio is within drift threshold.");
        }

        Console.WriteLine(Banner);
    }
}
