using System.Reflection;
using System.Text.Json;

namespace CCStats.Core.Services;

public sealed class UpdateCheckService
{
    private const string GitHubReleasesUrl = "https://api.github.com/repos/nicholasgriffintn/cc-stats/releases/latest";

    private readonly HttpClient _httpClient;

    public UpdateCheckService()
        : this(new HttpClient())
    {
    }

    public UpdateCheckService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CCStats-Windows/1.0");
    }

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(GitHubReleasesUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true,
            });

            if (release is null || string.IsNullOrEmpty(release.TagName))
            {
                return null;
            }

            var latestVersion = ParseVersion(release.TagName);
            var currentVersion = GetCurrentVersion();

            if (latestVersion is null || currentVersion is null)
            {
                return null;
            }

            if (latestVersion > currentVersion)
            {
                return new UpdateCheckResult
                {
                    LatestVersion = release.TagName,
                    DownloadUrl = release.HtmlUrl ?? string.Empty,
                    ReleaseNotes = release.Body,
                    PublishedAt = release.PublishedAt,
                };
            }

            return null; // Already up to date
        }
        catch
        {
            return null;
        }
    }

    private static Version? GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version;
    }

    private static Version? ParseVersion(string tagName)
    {
        var versionString = tagName.TrimStart('v', 'V');
        return Version.TryParse(versionString, out var version) ? version : null;
    }

    private sealed record GitHubRelease
    {
        public string? TagName { get; init; }
        public string? HtmlUrl { get; init; }
        public string? Body { get; init; }
        public DateTimeOffset? PublishedAt { get; init; }
    }
}

public sealed record UpdateCheckResult
{
    public required string LatestVersion { get; init; }
    public required string DownloadUrl { get; init; }
    public string? ReleaseNotes { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
}
