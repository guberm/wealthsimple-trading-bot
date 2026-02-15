using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OtpNet;
using WealthsimpleTradingBot.Configuration;

namespace WealthsimpleTradingBot.Auth;

public class AuthenticationException : Exception
{
    public AuthenticationException(string message) : base(message) { }
}

public class WealthsimpleAuthenticator : IAuthenticator
{
    private readonly HttpClient _httpClient;
    private readonly WealthsimpleSettings _settings;
    private readonly ILogger<WealthsimpleAuthenticator> _logger;
    private string? _accessToken;
    private string? _refreshToken;

    public WealthsimpleAuthenticator(
        HttpClient httpClient,
        IOptions<WealthsimpleSettings> settings,
        ILogger<WealthsimpleAuthenticator> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/'));
    }

    public string? AccessToken => _accessToken;
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    public async Task<(string AccessToken, string RefreshToken)> LoginAsync()
    {
        _logger.LogInformation("Attempting login for {Email}", _settings.Email);

        var payload = new Dictionary<string, string>
        {
            ["email"] = _settings.Email,
            ["password"] = _settings.Password
        };

        var response = await _httpClient.PostAsJsonAsync("/auth/login", payload);

        // If 2FA required, retry with OTP
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            && !string.IsNullOrEmpty(_settings.OtpSecret))
        {
            var otp = GenerateOtp();
            payload["otp"] = otp;
            _logger.LogInformation("2FA required, retrying with OTP");
            response = await _httpClient.PostAsJsonAsync("/auth/login", payload);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new AuthenticationException(
                $"Login failed with status {(int)response.StatusCode}: {errorBody}");
        }

        _accessToken = GetHeader(response, "X-Access-Token");
        _refreshToken = GetHeader(response, "X-Refresh-Token");

        // Fallback: check JSON body
        if (string.IsNullOrEmpty(_accessToken))
        {
            try
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                _accessToken = json.TryGetProperty("access_token", out var at)
                    ? at.GetString() : null;
                _refreshToken = json.TryGetProperty("refresh_token", out var rt)
                    ? rt.GetString() : null;
            }
            catch { }
        }

        if (string.IsNullOrEmpty(_accessToken))
            throw new AuthenticationException("No access token received");

        _logger.LogInformation("Login successful");
        return (_accessToken!, _refreshToken ?? "");
    }

    public async Task<string> RefreshTokenAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken))
            throw new AuthenticationException("No refresh token available");

        var payload = new Dictionary<string, string>
        {
            ["refresh_token"] = _refreshToken
        };

        var response = await _httpClient.PostAsJsonAsync("/auth/refresh", payload);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new AuthenticationException(
                $"Token refresh failed: {(int)response.StatusCode}: {errorBody}");
        }

        _accessToken = GetHeader(response, "X-Access-Token") ?? _accessToken;
        _refreshToken = GetHeader(response, "X-Refresh-Token") ?? _refreshToken;

        _logger.LogInformation("Token refreshed successfully");
        return _accessToken!;
    }

    private string GenerateOtp()
    {
        if (string.IsNullOrEmpty(_settings.OtpSecret))
            throw new AuthenticationException("OTP secret not configured but 2FA required");

        var secretBytes = Base32Encoding.ToBytes(_settings.OtpSecret);
        var totp = new Totp(secretBytes);
        return totp.ComputeTotp();
    }

    private static string? GetHeader(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;
    }
}
