using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCStats.Core.Models;
using System.Linq;

namespace CCStats.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class SecureStorageService
{
    private readonly string _filePath;

    public SecureStorageService()
        : this(GetDefaultFilePath())
    {
    }

    public SecureStorageService(string filePath)
    {
        _filePath = filePath;
    }

    public void SaveCredentials(StoredCredentials credentials)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(credentials);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, encryptedBytes);
    }

    public StoredCredentials? LoadCredentials()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var encryptedBytes = File.ReadAllBytes(_filePath);
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<StoredCredentials>(json);
        }
        catch
        {
            return null;
        }
    }

    public void ClearCredentials()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    // --- Multi-account support ---

    /// <summary>Lists all stored account IDs.</summary>
    public IReadOnlyList<string> ListAccounts()
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        if (!Directory.Exists(dir)) return Array.Empty<string>();

        return Directory.GetFiles(dir, "account_*.dat")
            .Select(f => Path.GetFileNameWithoutExtension(f)!.Replace("account_", ""))
            .ToList();
    }

    /// <summary>Saves credentials for a specific account.</summary>
    public void SaveAccountCredentials(string accountId, StoredCredentials credentials)
    {
        var path = GetAccountFilePath(accountId);
        var directory = Path.GetDirectoryName(path);
        if (directory is not null) Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(credentials);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encryptedBytes);
    }

    /// <summary>Loads credentials for a specific account.</summary>
    public StoredCredentials? LoadAccountCredentials(string accountId)
    {
        var path = GetAccountFilePath(accountId);
        if (!File.Exists(path)) return null;
        try
        {
            var encryptedBytes = File.ReadAllBytes(path);
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<StoredCredentials>(json);
        }
        catch { return null; }
    }

    /// <summary>Removes a specific account's credentials.</summary>
    public void RemoveAccount(string accountId)
    {
        var path = GetAccountFilePath(accountId);
        if (File.Exists(path)) File.Delete(path);
    }

    private string GetAccountFilePath(string accountId)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        return Path.Combine(dir, $"account_{accountId}.dat");
    }

    private static string GetDefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "CCStats", "credentials.dat");
    }
}

public sealed record StoredCredentials
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? SubscriptionType { get; init; }
    public RateLimitTier RateLimitTier { get; init; } = RateLimitTier.Unknown;
    public string? AccountId { get; init; }
    public string? DisplayName { get; init; }
}
