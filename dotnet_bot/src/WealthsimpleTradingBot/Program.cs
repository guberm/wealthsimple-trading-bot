using Quartz;
using Serilog;
using WealthsimpleTradingBot.Api;
using WealthsimpleTradingBot.Auth;
using WealthsimpleTradingBot.Configuration;
using WealthsimpleTradingBot.Execution;
using WealthsimpleTradingBot.Scheduling;
using WealthsimpleTradingBot.Services;
using WealthsimpleTradingBot.Strategy;

namespace WealthsimpleTradingBot;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .UseSerilog((ctx, config) =>
                config.ReadFrom.Configuration(ctx.Configuration))
            .ConfigureServices((ctx, services) =>
            {
                var config = ctx.Configuration;

                // Bind configuration
                services.Configure<WealthsimpleSettings>(config.GetSection("Wealthsimple"));
                services.Configure<TradingSettings>(config.GetSection("Trading"));
                services.Configure<ScheduleSettings>(config.GetSection("Schedule"));
                services.Configure<StockPickerSettings>(config.GetSection("StockPicker"));
                services.Configure<RebalancerSettings>(config.GetSection("Rebalancer"));
                services.Configure<SafetySettings>(config.GetSection("Safety"));
                services.Configure<StockUniverseSettings>(config.GetSection("StockUniverse"));

                // Load WS credentials from environment variables
                services.PostConfigure<WealthsimpleSettings>(ws =>
                {
                    ws.Email = Environment.GetEnvironmentVariable("WS__Email")
                        ?? ws.Email;
                    ws.Password = Environment.GetEnvironmentVariable("WS__Password")
                        ?? ws.Password;
                    ws.OtpSecret = Environment.GetEnvironmentVariable("WS__OtpSecret")
                        ?? ws.OtpSecret;
                });

                // Auth
                services.AddHttpClient<IAuthenticator, WealthsimpleAuthenticator>();
                services.AddSingleton<TokenManager>();

                // API client
                services.AddHttpClient<IWealthsimpleClient, WealthsimpleClient>();
                services.AddSingleton<IAccountService, AccountService>();
                services.AddSingleton<IMarketDataService, MarketDataService>();
                services.AddSingleton<IOrderService, OrderService>();

                // Yahoo Finance
                services.AddHttpClient<IYahooFinanceService, YahooFinanceService>();

                // Strategy
                services.AddSingleton<IStockPicker, StockPicker>();
                services.AddSingleton<IPortfolioRebalancer, PortfolioRebalancer>();

                // Execution
                services.AddSingleton(sp =>
                {
                    var safety = config.GetSection("Safety").Get<SafetySettings>()
                        ?? new SafetySettings();
                    return new RateLimiter(
                        safety.RateLimitPerHour, 3600,
                        sp.GetRequiredService<ILogger<RateLimiter>>());
                });
                services.AddSingleton<IOrderExecutor, OrderExecutor>();
                services.AddSingleton<DryRunSimulator>();

                // Main service
                services.AddSingleton<TradingBotService>();

                // Quartz scheduling
                var scheduleConfig = config.GetSection("Schedule").Get<ScheduleSettings>()
                    ?? new ScheduleSettings();

                if (scheduleConfig.Enabled && !args.Contains("--run-once"))
                {
                    services.AddQuartz(q =>
                    {
                        var jobKey = new JobKey("TradingJob");
                        q.AddJob<TradingJob>(opts => opts.WithIdentity(jobKey));

                        for (int i = 0; i < scheduleConfig.CronExpressions.Count; i++)
                        {
                            var cron = scheduleConfig.CronExpressions[i];
                            q.AddTrigger(opts => opts
                                .ForJob(jobKey)
                                .WithIdentity($"TradingJob-trigger-{i}")
                                .WithCronSchedule(cron, x =>
                                    x.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById(
                                        scheduleConfig.Timezone))));
                        }
                    });
                    services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
                }
            })
            .Build();

        // Handle --run-once flag
        if (args.Contains("--run-once"))
        {
            var bot = host.Services.GetRequiredService<TradingBotService>();
            if (args.Contains("--live"))
                bot.SetLiveOverride(true);

            Log.Information("Running trading pipeline once...");
            await bot.RunPipelineAsync();
            return;
        }

        // Handle --live flag
        if (args.Contains("--live"))
        {
            var bot = host.Services.GetRequiredService<TradingBotService>();
            bot.SetLiveOverride(true);
        }

        Log.Information("Wealthsimple Trading Bot starting with scheduled execution...");
        Log.Information("Schedule: every 2-3 hours during market hours (ET), Mon-Fri");
        await host.RunAsync();
    }
}
