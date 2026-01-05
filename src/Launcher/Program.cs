using Avalonia;
using Avalonia.Logging;
using Launcher.Extensions;
using Launcher.Helpers;
using Launcher.Services;
using Launcher.ViewModels;
using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.IO;
using Velopack;

namespace Launcher;
internal sealed class Program
{
    [STAThread]
    internal static void Main(string[] args)
    {
        // 1. Run Linux Self-Installer Check First
        Launcher.Services.LinuxSetup.CheckAndInstall();

        // 2. Setup Logging and Error Handling
        SetupNLog();
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // 3. Start Background Services
        if (Settings.Instance.DiscordActivity)
        {
            DiscordService.Start();
        }

        // 4. Start UI (Only call this ONCE)
        BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

        // 5. Cleanup
        LogManager.Shutdown();
    }

    internal static AppBuilder BuildAvaloniaApp()
    {
        VelopackApp.Build().Run();

        var builder = AppBuilder.Configure<App>()
        .WithInterFont()
        .UsePlatformDetect();

        #if DEBUG
        builder.LogToTrace();
        #endif

        builder.LogToNLog(LogEventLevel.Error);

        return builder;
    }

    private static void SetupNLog()
    {
        var config = new LoggingConfiguration();

        #if DEBUG
        var debuggerTarget = new DebuggerTarget("debugger");
        config.AddRule(LogLevel.Debug, LogLevel.Fatal, debuggerTarget);
        #endif

        var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        if (!Directory.Exists(logsDir))
        {
            Directory.CreateDirectory(logsDir);
        }

        var fileTarget = new FileTarget("file")
        {
            DeleteOldFileOnStartup = true,
            FileName = Path.Combine(logsDir, Constants.LogFile)
        };

        config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);

        LogManager.Configuration = config;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var logger = LogManager.GetCurrentClassLogger();

        if (e.ExceptionObject is Exception exception)
        {
            logger.Fatal(exception.ToString());
        }
        else
        {
            logger.Fatal("Unhandled exception of unknown type: {0}", e.ExceptionObject?.ToString() ?? "null");
        }
    }
}
