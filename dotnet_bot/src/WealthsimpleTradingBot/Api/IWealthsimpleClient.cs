namespace WealthsimpleTradingBot.Api;

public class WealthsimpleApiException : Exception
{
    public int StatusCode { get; }
    public WealthsimpleApiException(int statusCode, string message)
        : base($"WS API error {statusCode}: {message}")
    {
        StatusCode = statusCode;
    }
}

public interface IWealthsimpleClient
{
    Task<T> GetAsync<T>(string path, Dictionary<string, string>? queryParams = null);
    Task<T> PostAsync<T>(string path, object body);
    Task DeleteAsync(string path);
}
