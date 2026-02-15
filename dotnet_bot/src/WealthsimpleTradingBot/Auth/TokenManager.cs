namespace WealthsimpleTradingBot.Auth;

public class TokenManager
{
    private readonly IAuthenticator _authenticator;
    private readonly ILogger<TokenManager> _logger;
    private DateTime _lastAuthTime = DateTime.MinValue;

    private const int TokenLifetimeSeconds = 900;    // ~15 min
    private const int RefreshMarginSeconds = 120;     // refresh 2 min early

    public TokenManager(IAuthenticator authenticator, ILogger<TokenManager> logger)
    {
        _authenticator = authenticator;
        _logger = logger;
    }

    public async Task<string> GetValidTokenAsync()
    {
        if (!_authenticator.IsAuthenticated)
        {
            await EnsureAuthenticatedAsync();
        }
        else if (NeedsRefresh())
        {
            try
            {
                await _authenticator.RefreshTokenAsync();
                _lastAuthTime = DateTime.UtcNow;
                _logger.LogInformation("Token proactively refreshed");
            }
            catch (AuthenticationException)
            {
                _logger.LogWarning("Refresh failed, performing full re-login");
                await _authenticator.LoginAsync();
                _lastAuthTime = DateTime.UtcNow;
            }
        }

        return _authenticator.AccessToken
            ?? throw new AuthenticationException("No token available after auth");
    }

    public async Task EnsureAuthenticatedAsync()
    {
        await _authenticator.LoginAsync();
        _lastAuthTime = DateTime.UtcNow;
    }

    private bool NeedsRefresh()
    {
        var elapsed = (DateTime.UtcNow - _lastAuthTime).TotalSeconds;
        return elapsed > (TokenLifetimeSeconds - RefreshMarginSeconds);
    }
}
