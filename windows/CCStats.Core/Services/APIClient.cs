using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CCStats.Core.Services;

public sealed class APIClient : IDisposable
{
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    private const string ProfileEndpoint = "https://api.anthropic.com/api/oauth/profile";

    private static readonly string UserAgent = $"cc-hdrm/{GetAppVersion()}";

    private readonly HttpClient _httpClient;

    public APIClient()
        : this(new HttpClient())
    {
    }

    public APIClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("anthropic-beta", "oauth-2025-04-20");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public void SetAccessToken(string accessToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static string GetAppVersion()
    {
        var assembly = System.Reflection.Assembly.GetEntryAssembly();
        var version = assembly?.GetName().Version;
        return version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown";
    }

    public async Task<UsageResponse?> FetchUsageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(UsageEndpoint, cancellationToken);
            await ThrowIfRateLimited(response);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<UsageResponse>(json, JsonOptions);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ApiAuthenticationException("Access token is invalid or expired", ex);
        }
    }

    public async Task<ProfileResponse?> FetchProfileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(ProfileEndpoint, cancellationToken);
            await ThrowIfRateLimited(response);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<ProfileResponse>(json, JsonOptions);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ApiAuthenticationException("Access token is invalid or expired", ex);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static async Task ThrowIfRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
            var body = await response.Content.ReadAsStringAsync();
            throw new ApiRateLimitException(retryAfter, body);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}

/// Matches the JSON structure from /api/oauth/usage.
/// Top-level keys: five_hour, seven_day, seven_day_sonnet, extra_usage
public sealed record UsageResponse
{
    public WindowUsage? FiveHour { get; init; }
    public WindowUsage? SevenDay { get; init; }
    public WindowUsage? SevenDaySonnet { get; init; }
    public ExtraUsageData? ExtraUsage { get; init; }
}

public sealed record WindowUsage
{
    public double? Utilization { get; init; }
    public string? ResetsAt { get; init; }
}

/// Extra usage billing information.
/// Note: MonthlyLimit and UsedCredits are returned in cents (divide by 100 for currency display).
public sealed record ExtraUsageData
{
    public bool? IsEnabled { get; init; }
    public double? MonthlyLimit { get; init; }
    public double? UsedCredits { get; init; }
    public double? Utilization { get; init; }
}

/// Matches the JSON structure from /api/oauth/profile.
/// Top-level key: organization { organization_type, rate_limit_tier }
public sealed record ProfileResponse
{
    public ProfileOrganization? Organization { get; init; }
}

public sealed record ProfileOrganization
{
    public string? OrganizationType { get; init; }
    public string? RateLimitTier { get; init; }

    /// Maps organization_type API values to display-friendly subscription names.
    public string? SubscriptionTypeDisplay => OrganizationType switch
    {
        "claude_pro" => "pro",
        "claude_max" => "max",
        "claude_enterprise" => "enterprise",
        "claude_team" => "team",
        _ => null,
    };
}

public sealed class ApiAuthenticationException : Exception
{
    public ApiAuthenticationException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class ApiRateLimitException : Exception
{
    public TimeSpan RetryAfter { get; }

    public ApiRateLimitException(TimeSpan retryAfter, string? body = null)
        : base($"Rate limited. Retry after {retryAfter.TotalSeconds}s")
    {
        RetryAfter = retryAfter;
    }
}
