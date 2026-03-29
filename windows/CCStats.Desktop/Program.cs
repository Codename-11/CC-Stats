using Avalonia;
using ReactiveUI.Avalonia;
using System;
using System.Diagnostics;
using System.IO;

namespace CCStats.Desktop;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Redirect Debug.WriteLine to stderr so logs are visible in the terminal
        // (Debug output is invisible outside a debugger by default)
        Trace.Listeners.Add(new ConsoleTraceListener(useErrorStream: true));

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
}
