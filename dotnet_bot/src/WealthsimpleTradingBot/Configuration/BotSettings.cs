namespace WealthsimpleTradingBot.Configuration;

public class WealthsimpleSettings
{
    public string BaseUrl { get; set; } = "https://trade-service.wealthsimple.com";
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string OtpSecret { get; set; } = string.Empty;
}

public class TradingSettings
{
    public string Mode { get; set; } = "DryRun";
    public bool LiveModeConfirmation { get; set; } = false;
    public string AccountType { get; set; } = "ca_tfsa";

    public bool IsLiveMode => Mode.Equals("Live", StringComparison.OrdinalIgnoreCase)
                              && LiveModeConfirmation;
}

public class ScheduleSettings
{
    public bool Enabled { get; set; } = true;
    public List<string> CronExpressions { get; set; } = new()
    {
        "0 35 9 ? * MON-FRI",
        "0 30 11 ? * MON-FRI",
        "0 30 13 ? * MON-FRI",
        "0 30 15 ? * MON-FRI"
    };
    public string Timezone { get; set; } = "America/Toronto";
}

public class StockPickerSettings
{
    public int NumPicks { get; set; } = 7;
    public int MinMarketCapMillions { get; set; } = 500;
    public int MinAvgVolume { get; set; } = 100000;
    public int LookbackDays { get; set; } = 90;
    public bool SectorDiversity { get; set; } = true;
    public int MaxPerSector { get; set; } = 2;
    public bool PreferEtfs { get; set; } = true;
}

public class RebalancerSettings
{
    public string WeightStrategy { get; set; } = "Equal";
    public decimal DriftThresholdPct { get; set; } = 5.0m;
    public decimal MinTradeValueCad { get; set; } = 1.00m;
}

public class SafetySettings
{
    public decimal MaxSingleTradeCad { get; set; } = 5000.00m;
    public int MaxDailyTrades { get; set; } = 20;
    public int RateLimitPerHour { get; set; } = 6;
    public decimal RequireConfirmationAboveCad { get; set; } = 1000.00m;
}

public class StockUniverseSettings
{
    public List<string> Etfs { get; set; } = new();
    public List<string> Stocks { get; set; } = new();

    public List<string> GetAllSymbols() => Etfs.Concat(Stocks).ToList();
}
