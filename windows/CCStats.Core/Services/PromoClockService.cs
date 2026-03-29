using System.Text.Json;

namespace CCStats.Core.Services;

/// <summary>
/// Integrates with PromoClock (https://promoclock.co/en) to show peak/off-peak status.
/// When enabled, queries the PromoClock API for the current team's schedule status.
/// </summary>
public sealed class PromoClockService : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://promoclock.co/api/v1";

    public PromoClockService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CC-Stats/1.0");
    }

    /// <summary>
    /// Checks if the current time is in peak hours according to PromoClock.
    /// Returns null if the service is unavailable or not configured.
    /// </summary>
    public async Task<PromoClockStatus?> GetStatusAsync(string apiKey, string? teamId = null, CancellationToken ct = default)
    {
        try
        {
            var url = teamId is not null
                ? $"{BaseUrl}/status?team={Uri.EscapeDataString(teamId)}"
                : $"{BaseUrl}/status";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            var data = JsonSerializer.Deserialize<PromoClockResponse>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true,
            });

            if (data is null) return null;

            return new PromoClockStatus
            {
                IsPeak = data.IsPeak ?? false,
                PeriodName = data.PeriodName ?? (data.IsPeak == true ? "Peak" : "Off-Peak"),
                EndsAt = data.EndsAt,
                Timezone = data.Timezone,
            };
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record PromoClockResponse
    {
        public bool? IsPeak { get; init; }
        public string? PeriodName { get; init; }
        public string? EndsAt { get; init; }
        public string? Timezone { get; init; }
    }
}

public sealed class PromoClockStatus
{
    public bool IsPeak { get; init; }
    public string PeriodName { get; init; } = "";
    public string? EndsAt { get; init; }
    public string? Timezone { get; init; }
}
