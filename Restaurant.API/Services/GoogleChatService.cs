using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Restaurant.API.Services;

public class GoogleChatService : IGoogleChatService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleChatService> _logger;

    public GoogleChatService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GoogleChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAdminMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Message cannot be empty.", nameof(text));
        }

        string clientId = _configuration["GoogleChat:ClientId"]
            ?? _configuration["Authentication:Google:ClientId"]
            ?? string.Empty;
        string clientSecret = _configuration["GoogleChat:ClientSecret"]
            ?? _configuration["Authentication:Google:ClientSecret"]
            ?? string.Empty;
        string refreshToken = _configuration["GoogleChat:RefreshToken"] ?? string.Empty;
        string chatSpace = _configuration["GoogleChat:Space"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("Google OAuth client credentials are missing in configuration.");
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException("Google Chat refresh token is missing. Set GoogleChat:RefreshToken.");
        }

        if (string.IsNullOrWhiteSpace(chatSpace))
        {
            throw new InvalidOperationException("Google Chat space is missing. Set GoogleChat:Space (example: spaces/AAAA...).");
        }

        string accessToken = await GetAccessTokenAsync(clientId, clientSecret, refreshToken, cancellationToken);
        await PostMessageAsync(accessToken, chatSpace, text, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        var tokenBody = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(tokenBody)
        };

        using var tokenResponse = await client.SendAsync(tokenRequest, cancellationToken);
        var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!tokenResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Google OAuth token exchange failed. Status: {Status}. Body: {Body}", tokenResponse.StatusCode, tokenJson);
            throw new InvalidOperationException("Unable to authenticate with Google OAuth.");
        }

        using var tokenDoc = JsonDocument.Parse(tokenJson);
        if (!tokenDoc.RootElement.TryGetProperty("access_token", out var accessTokenElement))
        {
            throw new InvalidOperationException("Google OAuth response did not include an access token.");
        }

        return accessTokenElement.GetString() ?? throw new InvalidOperationException("Google OAuth access token was empty.");
    }

    private async Task PostMessageAsync(
        string accessToken,
        string chatSpace,
        string message,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var payload = JsonSerializer.Serialize(new { text = message });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://chat.googleapis.com/v1/{chatSpace}/messages")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Google Chat send failed. Status: {Status}. Body: {Body}", response.StatusCode, body);
            throw new InvalidOperationException("Failed to deliver message to Google Chat.");
        }
    }
}