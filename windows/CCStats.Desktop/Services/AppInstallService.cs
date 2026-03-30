using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using CCStats.Core.Services;
using Microsoft.Win32;

namespace CCStats.Desktop.Services;

/// <summary>
/// Handles first-launch registration: Start Menu shortcut, Add/Remove Programs entry.
/// Also provides uninstall to cleanly remove all system integration.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AppInstallService
{
    private const string AppName = "CC-Stats";
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CCStats";

    private static string StartMenuShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            $"{AppName}.lnk");

    private static string DesktopShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"{AppName}.lnk");

    /// <summary>
    /// Returns true if the app has already been registered (Start Menu shortcut exists).
    /// </summary>
    public static bool IsInstalled => File.Exists(StartMenuShortcutPath);

    /// <summary>
    /// Registers the app on first launch: creates Start Menu shortcut and Add/Remove Programs entry.
    /// Safe to call multiple times — skips if already installed.
    /// </summary>
    public static void EnsureInstalled()
    {
        if (IsInstalled) return;

        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            CreateShortcut(StartMenuShortcutPath, exePath);
            RegisterUninstall(exePath);
            AppLogger.Log("Install", $"Registered: Start Menu shortcut + Add/Remove Programs");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Install", "First-launch registration failed", ex);
        }
    }

    /// <summary>
    /// Updates registration to point to the current exe path.
    /// Call after self-update if the exe moved.
    /// </summary>
    public static void RefreshRegistration()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            // Update shortcut if it exists but points elsewhere
            if (File.Exists(StartMenuShortcutPath))
            {
                CreateShortcut(StartMenuShortcutPath, exePath);
            }

            RegisterUninstall(exePath);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Install", "Registration refresh failed", ex);
        }
    }

    /// <summary>
    /// Removes all system integration: shortcuts, registry entries, launch-at-login.
    /// Does NOT delete the exe or app data — user can do that manually.
    /// </summary>
    public static void Uninstall()
    {
        try
        {
            // Remove Start Menu shortcut
            if (File.Exists(StartMenuShortcutPath))
                File.Delete(StartMenuShortcutPath);

            // Remove desktop shortcut if it exists
            if (File.Exists(DesktopShortcutPath))
                File.Delete(DesktopShortcutPath);

            // Remove Add/Remove Programs entry
            try
            {
                Registry.CurrentUser.DeleteSubKey(UninstallKeyPath, false);
            }
            catch { /* Key doesn't exist — fine */ }

            // Remove launch-at-login
            LaunchAtLoginService.Disable();

            AppLogger.Log("Install", "Uninstalled: removed shortcuts, registry entries, startup key");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Install", "Uninstall cleanup failed", ex);
        }
    }

    /// <summary>
    /// Creates a .lnk shortcut file using Windows Script Host COM interop.
    /// </summary>
    private static void CreateShortcut(string shortcutPath, string targetExe)
    {
        var dir = Path.GetDirectoryName(shortcutPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Use WScript.Shell COM object to create .lnk files
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            var shortcut = shell.CreateShortcut(shortcutPath);
            try
            {
                shortcut.TargetPath = targetExe;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe);
                shortcut.Description = "Claude API headroom monitor for Windows";
                shortcut.IconLocation = $"{targetExe},0";
                shortcut.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    /// <summary>
    /// Registers in Add/Remove Programs (HKCU — no admin required).
    /// </summary>
    private static void RegisterUninstall(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        if (key == null) return;

        var version = typeof(AppInstallService).Assembly.GetName().Version;
        var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";

        key.SetValue("DisplayName", "CC-Stats (Windows)");
        key.SetValue("DisplayVersion", versionStr);
        key.SetValue("Publisher", "Codename-11");
        key.SetValue("DisplayIcon", $"{exePath},0");
        key.SetValue("InstallLocation", Path.GetDirectoryName(exePath) ?? "");
        key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(new FileInfo(exePath).Length / 1024), RegistryValueKind.DWord);
    }
}
