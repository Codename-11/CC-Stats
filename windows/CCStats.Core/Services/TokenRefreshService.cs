using System.Text.Json;

namespace CCStats.Core.Services;

public sealed class TokenRefreshService : IDisposable
{
    private const string TokenEndpoint = "https://console.anthropic.com/v1/oauth/token";
    /// Anthropic's public OAuth client ID (used by Claude Code and OpenCode)
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public TokenRefreshService()
        : this(new HttpClient(), ownsClient: true)
    {
    }

    public TokenRefreshService(HttpClient httpClient, bool ownsClient = false)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsClient;
        // NOTE: Do NOT send anthropic-beta on the token endpoint — upstream doesn't
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    public static bool IsTokenExpired(DateTimeOffset? expiresAt)
    {
        if (expiresAt is null)
        {
            return true;
        }

        return DateTimeOffset.UtcNow >= expiresAt.Value - ExpiryBuffer;
    }

    public async Task<TokenRefreshResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = refreshToken,
            };
            var content = new StringContent(
                JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(TokenEndpoint, content, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return TokenRefreshResult.Failure($"Refresh failed with status {response.StatusCode}");
            }

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });

            if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                return TokenRefreshResult.Failure("Invalid token response");
            }

            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            return TokenRefreshResult.Success(
                tokenResponse.AccessToken,
                tokenResponse.RefreshToken,
                expiresAt);
        }
        catch (Exception ex)
        {
            return TokenRefreshResult.Failure(ex.Message);
        }
    }

    private sealed record TokenResponse
    {
        public string? AccessToken { get; init; }
        public string? RefreshToken { get; init; }
        public int ExpiresIn { get; init; }
        public string? TokenType { get; init; }
    }
}

public sealed record TokenRefreshResult
{
    public bool IsSuccess { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? Error { get; init; }

    public static TokenRefreshResult Success(string accessToken, string? refreshToken, DateTimeOffset expiresAt) => new()
    {
        IsSuccess = true,
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        ExpiresAt = expiresAt,
    };

    public static TokenRefreshResult Failure(string error) => new()
    {
        IsSuccess = false,
        Error = error,
    };
}
