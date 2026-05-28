using System;
using Avalonia;
using Stranichnik.Diagnostics;
using Stranichnik.Settings;

namespace Stranichnik;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Logs.Configure(ShouldPrintLogsToConsole(args));
        Logs.Print("Application starting.");
        Logs.Print($"Application data directory: {AppDataPaths.AppDataDirectory}");

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        Logs.Print("Application stopped.");
    }

    private static bool ShouldPrintLogsToConsole(string[] args)
    {
        return Array.Exists(
            args,
            arg => string.Equals(arg, "--print-logs-to-console", StringComparison.Ordinal));
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
