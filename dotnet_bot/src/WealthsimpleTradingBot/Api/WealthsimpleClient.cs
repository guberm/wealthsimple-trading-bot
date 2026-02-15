using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WealthsimpleTradingBot.Auth;
using WealthsimpleTradingBot.Configuration;

namespace WealthsimpleTradingBot.Api;

public class WealthsimpleClient : IWealthsimpleClient
{
    private readonly HttpClient _httpClient;
    private readonly TokenManager _tokenManager;
    private readonly ILogger<WealthsimpleClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public WealthsimpleClient(
        HttpClient httpClient,
        TokenManager tokenManager,
        IOptions<WealthsimpleSettings> settings,
        ILogger<WealthsimpleClient> logger)
    {
        _httpClient = httpClient;
        _tokenManager = tokenManager;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(settings.Value.BaseUrl.TrimEnd('/'));
    }

    public async Task<T> GetAsync<T>(string path, Dictionary<string, string>? queryParams = null)
    {
        var url = BuildUrl(path, queryParams);
        _logger.LogDebug("GET {Url}", url);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        await SetAuthHeader(request);
        var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogInformation("Got 401, refreshing token and retrying");
            await _tokenManager.EnsureAuthenticatedAsync();
            request = new HttpRequestMessage(HttpMethod.Get, url);
            await SetAuthHeader(request);
            response = await _httpClient.SendAsync(request);
        }

        return await HandleResponse<T>(response);
    }

    public async Task<T> PostAsync<T>(string path, object body)
    {
        _logger.LogDebug("POST {Path}", path);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        await SetAuthHeader(request);
        var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogInformation("Got 401, refreshing token and retrying");
            await _tokenManager.EnsureAuthenticatedAsync();
            request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            };
            await SetAuthHeader(request);
            response = await _httpClient.SendAsync(request);
        }

        return await HandleResponse<T>(response);
    }

    public async Task DeleteAsync(string path)
    {
        _logger.LogDebug("DELETE {Path}", path);

        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        await SetAuthHeader(request);
        var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await _tokenManager.EnsureAuthenticatedAsync();
            request = new HttpRequestMessage(HttpMethod.Delete, path);
            await SetAuthHeader(request);
            response = await _httpClient.SendAsync(request);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new WealthsimpleApiException((int)response.StatusCode, errorBody);
        }
    }

    private async Task SetAuthHeader(HttpRequestMessage request)
    {
        var token = await _tokenManager.GetValidTokenAsync();
        request.Headers.TryAddWithoutValidation("Authorization", token);
    }

    private static string BuildUrl(string path, Dictionary<string, string>? queryParams)
    {
        if (queryParams == null || queryParams.Count == 0)
            return path;

        var query = string.Join("&", queryParams.Select(
            kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{path}?{query}";
    }

    private static async Task<T> HandleResponse<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new WealthsimpleApiException((int)response.StatusCode, errorBody);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        return result ?? throw new WealthsimpleApiException(
            (int)response.StatusCode, "Null response body");
    }
}
