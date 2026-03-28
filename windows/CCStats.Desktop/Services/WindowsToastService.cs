using System;
using System.Runtime.Versioning;
using CCStats.Core.Services;
using Microsoft.Toolkit.Uwp.Notifications;

namespace CCStats.Desktop.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsToastService : IDisposable
{
    private readonly NotificationService _notificationService;

    public WindowsToastService(NotificationService notificationService)
    {
        _notificationService = notificationService;
        _notificationService.NotificationRequested += OnNotificationRequested;
    }

    private void OnNotificationRequested(object? sender, NotificationEventArgs e)
    {
        ShowToast(e.Title, e.Body);
    }

    private void ShowToast(string title, string body)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show();
        }
        catch
        {
            // Toast notification is best-effort, don't crash the app
        }
    }

    public void Dispose()
    {
        _notificationService.NotificationRequested -= OnNotificationRequested;
    }
}
