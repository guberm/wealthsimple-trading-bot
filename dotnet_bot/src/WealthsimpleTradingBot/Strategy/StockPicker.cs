using Microsoft.Extensions.Options;
using WealthsimpleTradingBot.Configuration;
using WealthsimpleTradingBot.Models;
using WealthsimpleTradingBot.Services;

namespace WealthsimpleTradingBot.Strategy;

public interface IStockPicker
{
    Task<List<StockScore>> PickStocksAsync(int numPicks = 0);
}

public class StockPicker : IStockPicker
{
    private const double RiskFreeRate = 0.04;
    private readonly IYahooFinanceService _yahooService;
    private readonly StockPickerSettings _settings;
    private readonly StockUniverseSettings _universe;
    private readonly HashSet<string> _etfSymbols;
    private readonly ILogger<StockPicker> _logger;

    public StockPicker(
        IYahooFinanceService yahooService,
        IOptions<StockPickerSettings> settings,
        IOptions<StockUniverseSettings> universe,
        ILogger<StockPicker> logger)
    {
        _yahooService = yahooService;
        _settings = settings.Value;
        _universe = universe.Value;
        _etfSymbols = _universe.Etfs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<List<StockScore>> PickStocksAsync(int numPicks = 0)
    {
        if (numPicks == 0) numPicks = _settings.NumPicks;

        var allSymbols = _universe.GetAllSymbols();
        _logger.LogInformation("Picking top {NumPicks} from {Total} securities (lookback={Days}d)",
            numPicks, allSymbols.Count, _settings.LookbackDays);

        // 1. Fetch historical data
        var histData = await _yahooService.FetchHistoricalDataAsync(allSymbols, _settings.LookbackDays);

        // 2. Calculate metrics
        var scores = new List<StockScore>();
        foreach (var symbol in allSymbols)
        {
            if (!histData.TryGetValue(symbol, out var bars) || bars.Count < 2) continue;

            try
            {
                scores.Add(CalculateMetrics(symbol, bars));
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error for {Symbol}: {Error}", symbol, ex.Message);
            }
        }

        // 3. Filter
        scores = ApplyFilters(scores);
        if (scores.Count == 0)
        {
            _logger.LogError("No securities passed filters!");
            return new List<StockScore>();
        }

        // 4. Rank and compute composite score
        scores = RankAndScore(scores);

        // 5. Sort descending
        scores = scores.OrderByDescending(s => s.CompositeScore).ToList();

        // 6. Sector diversity
        if (_settings.SectorDiversity)
            scores = ApplySectorDiversity(scores, _settings.MaxPerSector);

        // 7. Top N
        var picks = scores.Take(numPicks).ToList();
        foreach (var (s, i) in picks.Select((s, i) => (s, i)))
        {
            _logger.LogInformation("  {Rank}. {Symbol} (score={Score:F4}, 90d={R90:P1}, sharpe={Sharpe:F2}, sector={Sector})",
                i + 1, s.Symbol, s.CompositeScore, s.Return90d, s.SharpeRatio, s.Sector);
        }

        return picks;
    }

    private StockScore CalculateMetrics(string symbol, List<HistoricalBar> bars)
    {
        var closes = bars.Select(b => (double)b.Close).ToList();
        var volumes = bars.Select(b => (double)b.Volume).ToList();

        double return90d = closes.Count > 1 ? (closes[^1] / closes[0]) - 1 : 0;
        int idx30d = Math.Max(0, closes.Count - 22);
        double return30d = closes.Count > idx30d ? (closes[^1] / closes[idx30d]) - 1 : 0;

        // Daily returns
        var dailyReturns = new List<double>();
        for (int i = 1; i < closes.Count; i++)
            dailyReturns.Add(closes[i] / closes[i - 1] - 1);

        double volatility = dailyReturns.Count > 1
            ? StdDev(dailyReturns) * Math.Sqrt(252)
            : 999;

        double annReturn = return90d * (365.0 / 90.0);
        double sharpe = volatility > 0 ? (annReturn - RiskFreeRate) / volatility : 0;
        double avgVol = volumes.Count > 0 ? volumes.Average() : 0;

        bool isEtf = _etfSymbols.Contains(symbol);

        return new StockScore(
            Symbol: symbol,
            Name: symbol,
            Sector: isEtf ? "ETF" : "Unknown",
            MarketCap: 0,
            AvgVolume: avgVol,
            Return90d: return90d,
            Return30d: return30d,
            Volatility: volatility,
            SharpeRatio: sharpe,
            CompositeScore: 0,
            IsEtf: isEtf
        );
    }

    private List<StockScore> ApplyFilters(List<StockScore> scores)
    {
        double minVol = _settings.MinAvgVolume;
        var filtered = scores.Where(s => s.AvgVolume >= minVol).ToList();
        _logger.LogInformation("After filters: {Count}/{Total} passed", filtered.Count, scores.Count);
        return filtered;
    }

    private List<StockScore> RankAndScore(List<StockScore> scores)
    {
        int n = scores.Count;
        if (n == 0) return scores;

        double[] PercentileRank(Func<StockScore, double> selector)
        {
            var indexed = scores.Select((s, i) => (i, val: selector(s)))
                .OrderBy(x => x.val).ToList();
            var ranks = new double[n];
            for (int r = 0; r < indexed.Count; r++)
                ranks[indexed[r].i] = (double)r / Math.Max(n - 1, 1);
            return ranks;
        }

        var mom90 = PercentileRank(s => s.Return90d);
        var sharpe = PercentileRank(s => s.SharpeRatio);
        var mom30 = PercentileRank(s => s.Return30d);
        var vol = PercentileRank(s => s.AvgVolume);
        var invVol = PercentileRank(s => -s.Volatility);

        return scores.Select((s, i) =>
        {
            double composite = 0.30 * mom90[i]
                             + 0.25 * sharpe[i]
                             + 0.20 * mom30[i]
                             + 0.15 * vol[i]
                             + 0.10 * invVol[i];

            if (_settings.PreferEtfs && s.IsEtf)
                composite += 0.10;

            return s with { CompositeScore = composite };
        }).ToList();
    }

    private List<StockScore> ApplySectorDiversity(List<StockScore> scores, int maxPerSector)
    {
        var sectorCounts = new Dictionary<string, int>();
        var result = new List<StockScore>();

        foreach (var s in scores)
        {
            var count = sectorCounts.GetValueOrDefault(s.Sector, 0);
            if (count < maxPerSector)
            {
                result.Add(s);
                sectorCounts[s.Sector] = count + 1;
            }
        }

        return result;
    }

    private static double StdDev(List<double> values)
    {
        double mean = values.Average();
        double sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / (values.Count - 1));
    }
}
