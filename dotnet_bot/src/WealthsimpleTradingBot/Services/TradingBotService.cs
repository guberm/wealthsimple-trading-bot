using Microsoft.Extensions.Options;
using WealthsimpleTradingBot.Api;
using WealthsimpleTradingBot.Auth;
using WealthsimpleTradingBot.Configuration;
using WealthsimpleTradingBot.Execution;
using WealthsimpleTradingBot.Strategy;

namespace WealthsimpleTradingBot.Services;

public class TradingBotService
{
    private readonly TokenManager _tokenManager;
    private readonly IAccountService _accountService;
    private readonly IMarketDataService _marketDataService;
    private readonly IStockPicker _stockPicker;
    private readonly IPortfolioRebalancer _rebalancer;
    private readonly IOrderExecutor _orderExecutor;
    private readonly DryRunSimulator _dryRunSimulator;
    private readonly TradingSettings _tradingSettings;
    private readonly ILogger<TradingBotService> _logger;
    private bool _liveOverride;

    public TradingBotService(
        TokenManager tokenManager,
        IAccountService accountService,
        IMarketDataService marketDataService,
        IStockPicker stockPicker,
        IPortfolioRebalancer rebalancer,
        IOrderExecutor orderExecutor,
        DryRunSimulator dryRunSimulator,
        IOptions<TradingSettings> tradingSettings,
        ILogger<TradingBotService> logger)
    {
        _tokenManager = tokenManager;
        _accountService = accountService;
        _marketDataService = marketDataService;
        _stockPicker = stockPicker;
        _rebalancer = rebalancer;
        _orderExecutor = orderExecutor;
        _dryRunSimulator = dryRunSimulator;
        _tradingSettings = tradingSettings.Value;
        _logger = logger;
    }

    public void SetLiveOverride(bool live) => _liveOverride = live;

    private bool IsLive => _tradingSettings.IsLiveMode && _liveOverride;

    public async Task RunPipelineAsync(CancellationToken ct = default)
    {
        _logger.LogInformation(new string('=', 60));
        _logger.LogInformation("TRADING PIPELINE START — mode={Mode}",
            IsLive ? "LIVE" : "DRY RUN");
        _logger.LogInformation(new string('=', 60));

        try
        {
            // 1. Authenticate
            _logger.LogInformation("Step 1: Authenticating with Wealthsimple...");
            await _tokenManager.EnsureAuthenticatedAsync();

            // 2. Get account and positions
            _logger.LogInformation("Step 2: Fetching account and positions...");
            var account = await _accountService.GetAccountByTypeAsync(_tradingSettings.AccountType);
            if (account == null)
            {
                _logger.LogError("Account type '{Type}' not found!", _tradingSettings.AccountType);
                return;
            }

            var positions = await _accountService.GetPositionsAsync(account.Id);
            var cashBalance = account.BuyingPower.Amount;
            var totalValue = await _accountService.GetTotalPortfolioValueAsync(
                account.Id, positions);

            _logger.LogInformation(
                "Account: {Type} | Cash: {Cash:C} | Positions: {Count} | Total: {Total:C}",
                account.AccountType, cashBalance, positions.Count, totalValue);

            // 3. Pick stocks
            _logger.LogInformation("Step 3: Running stock picker...");
            var picks = await _stockPicker.PickStocksAsync();
            if (picks.Count == 0)
            {
                _logger.LogError("Stock picker returned no picks!");
                return;
            }

            var selectedSymbols = picks.Select(p => p.Symbol).ToList();
            _logger.LogInformation("Selected: {Symbols}", string.Join(", ", selectedSymbols));

            // 4. Resolve security IDs and prices
            _logger.LogInformation("Step 4: Resolving security IDs and prices...");
            var securityIds = await _marketDataService.BulkResolveSecuritiesAsync(selectedSymbols);
            var prices = await _marketDataService.GetBulkQuotesAsync(selectedSymbols);

            // 5. Calculate targets
            _logger.LogInformation("Step 5: Calculating portfolio targets...");
            var summary = _rebalancer.CalculateTargets(
                picks, positions, cashBalance, prices, securityIds);
            var (sellOrders, buyOrders) = _rebalancer.GenerateOrders(summary);

            // 6. Execute or simulate
            if (IsLive)
            {
                _logger.LogInformation("Step 6: LIVE EXECUTION");
                if (!ConfirmLiveTrading(sellOrders, buyOrders))
                {
                    _logger.LogInformation("Live trading cancelled by user");
                    return;
                }
                var results = await _orderExecutor.ExecuteOrdersAsync(sellOrders, buyOrders, ct);
                var execSummary = _orderExecutor.GetExecutionSummary();
                _logger.LogInformation("Execution: {Summary}", execSummary);
            }
            else
            {
                _logger.LogInformation("Step 6: DRY RUN SIMULATION");
                _dryRunSimulator.SimulateOrders(sellOrders, buyOrders, cashBalance, prices);
                _dryRunSimulator.PrintSimulationReport(summary);
            }

            _logger.LogInformation("TRADING PIPELINE COMPLETE");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed: {Error}", ex.Message);
        }
    }

    private bool ConfirmLiveTrading(
        List<Models.OrderRequest> sellOrders, List<Models.OrderRequest> buyOrders)
    {
        Console.WriteLine();
        Console.WriteLine(new string('!', 60));
        Console.WriteLine("  WARNING: LIVE TRADING MODE");
        Console.WriteLine(new string('!', 60));
        Console.WriteLine($"\n  Sell orders: {sellOrders.Count}");
        foreach (var o in sellOrders)
            Console.WriteLine($"    SELL {o.Quantity} x {o.Symbol} @ {o.LimitPrice:C}");
        Console.WriteLine($"\n  Buy orders: {buyOrders.Count}");
        foreach (var o in buyOrders)
            Console.WriteLine($"    BUY  {o.Quantity} x {o.Symbol} @ {o.LimitPrice:C}");
        Console.WriteLine("\n  Type 'YES' to proceed or anything else to cancel:");

        try
        {
            Console.Write("  > ");
            var response = Console.ReadLine()?.Trim();
            return response == "YES";
        }
        catch
        {
            return false;
        }
    }
}
