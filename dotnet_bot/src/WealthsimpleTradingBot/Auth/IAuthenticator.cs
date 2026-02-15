namespace WealthsimpleTradingBot.Auth;

public interface IAuthenticator
{
    Task<(string AccessToken, string RefreshToken)> LoginAsync();
    Task<string> RefreshTokenAsync();
    string? AccessToken { get; }
    bool IsAuthenticated { get; }
}
