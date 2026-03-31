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
    private readonly object _ioLock = new();

    /// <summary>
    /// Set when credential decryption fails (e.g. machine migration or user account change).
    /// The UI layer can read this to warn the user. Call <see cref="ClearDecryptionError"/> after handling.
    /// </summary>
    public static string? LastDecryptionError { get; private set; }

    /// <summary>Clears <see cref="LastDecryptionError"/> after the UI has shown the warning.</summary>
    public static void ClearDecryptionError() => LastDecryptionError = null;

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
        lock (_ioLock)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(credentials);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            var tempPath = _filePath + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, encryptedBytes);
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch
            {
                try { File.Delete(tempPath); } catch { }
                throw;
            }
        }
    }

    public StoredCredentials? LoadCredentials()
    {
        lock (_ioLock)
        {
            LastDecryptionError = null;

            if (!File.Exists(_filePath))
            {
                return null;
            }

            byte[]? plainBytes = null;
            try
            {
                var encryptedBytes = File.ReadAllBytes(_filePath);
                plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(plainBytes);
                return JsonSerializer.Deserialize<StoredCredentials>(json);
            }
            catch (CryptographicException ex)
            {
                LastDecryptionError = "Credentials encrypted on a different machine or user account — please sign in again";
                AppLogger.Error("SecureStorage", "Failed to decrypt credentials (data may be corrupt or from another user)", ex);
                return null;
            }
            catch (JsonException ex)
            {
                AppLogger.Error("SecureStorage", "Failed to deserialize credentials JSON", ex);
                return null;
            }
            catch (Exception ex)
            {
                LastDecryptionError = $"Failed to load credentials: {ex.Message}";
                AppLogger.Error("SecureStorage", "Failed to load credentials", ex);
                return null;
            }
            finally
            {
                if (plainBytes is not null)
                    Array.Clear(plainBytes, 0, plainBytes.Length);
            }
        }
    }

    public void ClearCredentials()
    {
        lock (_ioLock)
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
    }

    // --- Multi-account support ---

    /// <summary>Lists all stored account IDs.</summary>
    public IReadOnlyList<string> ListAccounts()
    {
        lock (_ioLock)
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir)) return Array.Empty<string>();

            return Directory.GetFiles(dir, "account_*.dat")
                .Select(f => Path.GetFileNameWithoutExtension(f)!.Replace("account_", ""))
                .ToList();
        }
    }

    /// <summary>Saves credentials for a specific account.</summary>
    public void SaveAccountCredentials(string accountId, StoredCredentials credentials)
    {
        lock (_ioLock)
        {
            var path = GetAccountFilePath(accountId);
            var directory = Path.GetDirectoryName(path);
            if (directory is not null) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(credentials);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            var tempPath = path + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, encryptedBytes);
                File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(tempPath); } catch { }
                throw;
            }
        }
    }

    /// <summary>Loads credentials for a specific account.</summary>
    public StoredCredentials? LoadAccountCredentials(string accountId)
    {
        lock (_ioLock)
        {
            LastDecryptionError = null;

            var path = GetAccountFilePath(accountId);
            if (!File.Exists(path)) return null;
            byte[]? plainBytes = null;
            try
            {
                var encryptedBytes = File.ReadAllBytes(path);
                plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(plainBytes);
                return JsonSerializer.Deserialize<StoredCredentials>(json);
            }
            catch (CryptographicException ex)
            {
                LastDecryptionError = "Credentials encrypted on a different machine or user account — please sign in again";
                AppLogger.Error("SecureStorage", $"Failed to decrypt account credentials for {accountId} (data may be corrupt or from another user)", ex);
                return null;
            }
            catch (JsonException ex)
            {
                AppLogger.Error("SecureStorage", $"Failed to deserialize account credentials JSON for {accountId}", ex);
                return null;
            }
            catch (Exception ex)
            {
                LastDecryptionError = $"Failed to load credentials: {ex.Message}";
                AppLogger.Error("SecureStorage", $"Failed to load account credentials for {accountId}", ex);
                return null;
            }
            finally
            {
                if (plainBytes is not null)
                    Array.Clear(plainBytes, 0, plainBytes.Length);
            }
        }
    }

    /// <summary>Removes a specific account's credentials.</summary>
    public void RemoveAccount(string accountId)
    {
        lock (_ioLock)
        {
            var path = GetAccountFilePath(accountId);
            if (File.Exists(path)) File.Delete(path);
        }
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
