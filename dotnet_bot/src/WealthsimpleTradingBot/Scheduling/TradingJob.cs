using Quartz;
using WealthsimpleTradingBot.Services;

namespace WealthsimpleTradingBot.Scheduling;

[DisallowConcurrentExecution]
public class TradingJob : IJob
{
    private readonly TradingBotService _botService;
    private readonly ILogger<TradingJob> _logger;

    public TradingJob(TradingBotService botService, ILogger<TradingJob> logger)
    {
        _botService = botService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Scheduled trading job triggered at {Time}", DateTime.Now);
        await _botService.RunPipelineAsync(context.CancellationToken);
        _logger.LogInformation("Scheduled trading job completed at {Time}", DateTime.Now);
    }
}
