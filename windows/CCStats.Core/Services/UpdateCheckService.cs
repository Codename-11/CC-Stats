using System.Reflection;
using System.Text.Json;

namespace CCStats.Core.Services;

public sealed class UpdateCheckService
{
    private const string GitHubReleasesUrl = "https://api.github.com/repos/Codename-11/CC-Stats/releases/latest";

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
                // Find the .exe asset download URL
                var exeAsset = release.Assets?.FirstOrDefault(a =>
                    a.BrowserDownloadUrl?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);

                return new UpdateCheckResult
                {
                    LatestVersion = release.TagName,
                    DownloadUrl = release.HtmlUrl ?? string.Empty,
                    ExeDownloadUrl = exeAsset?.BrowserDownloadUrl,
                    ReleaseNotes = release.Body,
                    PublishedAt = release.PublishedAt,
                };
            }

            return null; // Already up to date
        }
        catch (Exception ex)
        {
            AppLogger.Error("UpdateCheck", "Failed to check for updates", ex);
            return null;
        }
    }

    /// <summary>
    /// Downloads the latest release exe to a temp file.
    /// Returns the path to the downloaded file, or null on failure.
    /// </summary>
    public async Task<string?> DownloadUpdateAsync(string downloadUrl, CancellationToken cancellationToken = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"CCStats-update-{Guid.NewGuid():N}.exe");
        try
        {
            // Use the provided download URL directly instead of re-fetching the release
            var bytes = await _httpClient.GetByteArrayAsync(downloadUrl, cancellationToken);
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);

            return tempPath;
        }
        catch (Exception ex)
        {
            AppLogger.Error("UpdateCheck", "Failed to download update", ex);

            // Clean up temp file on failure
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* best effort cleanup */ }

            return null;
        }
    }

    /// <summary>
    /// Replaces the current exe with the downloaded update and restarts.
    /// Uses rename-swap: rename running exe -> move new exe -> restart.
    /// </summary>
    public static void ApplyUpdateAndRestart(string downloadedExePath)
    {
        var currentExe = Environment.ProcessPath;
        if (currentExe is null) return;

        var backupPath = currentExe + ".old";

        // Escape paths for safe use in batch scripts
        var safeCurrentExe = EscapeBatchString(currentExe);
        var safeBackupPath = EscapeBatchString(backupPath);
        var safeDownloadedExe = EscapeBatchString(downloadedExePath);

        // Create a batch script to do the swap after we exit
        var script = Path.Combine(Path.GetTempPath(), "ccstats-update.cmd");
        File.WriteAllText(script, $"""
            @echo off
            timeout /t 2 /nobreak >nul
            del "{safeBackupPath}" 2>nul
            move "{safeCurrentExe}" "{safeBackupPath}"
            if errorlevel 1 (
                echo Failed to back up current exe, aborting update.
                exit /b 1
            )
            copy "{safeDownloadedExe}" "{safeCurrentExe}"
            if errorlevel 1 (
                echo Copy failed, restoring from backup.
                move "{safeBackupPath}" "{safeCurrentExe}" 2>nul
                exit /b 1
            )
            start "" "{safeCurrentExe}"
            if errorlevel 1 (
                echo Start failed, restoring from backup.
                del "{safeCurrentExe}" 2>nul
                move "{safeBackupPath}" "{safeCurrentExe}" 2>nul
                exit /b 1
            )
            del "{safeDownloadedExe}" 2>nul
            del "%~f0"
            """);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{script}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        });

        // Exit the current app — the script will restart it
        Environment.Exit(0);
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

        // Strip pre-release suffix (e.g., "1.2.3-beta.1" or "1.2.3+build.456")
        var dashIndex = versionString.IndexOf('-');
        if (dashIndex >= 0) versionString = versionString[..dashIndex];
        var plusIndex = versionString.IndexOf('+');
        if (plusIndex >= 0) versionString = versionString[..plusIndex];

        return Version.TryParse(versionString, out var version) ? version : null;
    }

    /// <summary>
    /// Escapes special batch script characters to prevent command injection.
    /// </summary>
    private static string EscapeBatchString(string value)
    {
        return value
            .Replace("^", "^^")
            .Replace("&", "^&")
            .Replace("|", "^|")
            .Replace("<", "^<")
            .Replace(">", "^>")
            .Replace("%", "%%");
    }

    private sealed record GitHubRelease
    {
        public string? TagName { get; init; }
        public string? HtmlUrl { get; init; }
        public string? Body { get; init; }
        public DateTimeOffset? PublishedAt { get; init; }
        public List<GitHubAsset>? Assets { get; init; }
    }

    private sealed record GitHubAsset
    {
        public string? Name { get; init; }
        public string? BrowserDownloadUrl { get; init; }
        public long Size { get; init; }
    }
}

public sealed record UpdateCheckResult
{
    public required string LatestVersion { get; init; }
    public required string DownloadUrl { get; init; }
    public string? ExeDownloadUrl { get; init; }
    public string? ReleaseNotes { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
}
