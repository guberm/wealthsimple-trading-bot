namespace WealthsimpleTradingBot.Execution;

public class RateLimiter
{
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly Queue<DateTime> _timestamps = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ILogger<RateLimiter> _logger;

    public RateLimiter(int maxRequests, int windowSeconds, ILogger<RateLimiter> logger)
    {
        _maxRequests = maxRequests;
        _window = TimeSpan.FromSeconds(windowSeconds);
        _logger = logger;
    }

    public async Task AcquireAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            PruneExpired();
            while (_timestamps.Count >= _maxRequests)
            {
                var oldest = _timestamps.Peek();
                var waitTime = _window - (DateTime.UtcNow - oldest) + TimeSpan.FromSeconds(1);
                _logger.LogWarning(
                    "Rate limit reached ({Count}/{Max}). Waiting {Wait}s...",
                    _timestamps.Count, _maxRequests, waitTime.TotalSeconds);
                _semaphore.Release();
                await Task.Delay(waitTime, ct);
                await _semaphore.WaitAsync(ct);
                PruneExpired();
            }
            _timestamps.Enqueue(DateTime.UtcNow);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public bool CanProceed
    {
        get
        {
            PruneExpired();
            return _timestamps.Count < _maxRequests;
        }
    }

    public int RemainingCapacity
    {
        get
        {
            PruneExpired();
            return Math.Max(0, _maxRequests - _timestamps.Count);
        }
    }

    private void PruneExpired()
    {
        var cutoff = DateTime.UtcNow - _window;
        while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
            _timestamps.Dequeue();
    }
}
