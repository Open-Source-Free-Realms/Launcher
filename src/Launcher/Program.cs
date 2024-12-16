using System;
using System.Reflection;
using Avalonia;
using Avalonia.Logging;
using Launcher.Extensions;
using Launcher.Services;
using Launcher.ViewModels;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using Velopack;

namespace Launcher;

internal sealed class Program
{
    [STAThread]
    internal static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        LogManager.Setup().LoadConfigurationFromAssemblyResource(typeof(Program).GetTypeInfo().Assembly);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(LogManager.Configuration));

        VelopackApp.Build().Run(loggerFactory.CreateLogger<VelopackApp>());

        if (Settings.Instance.DiscordActivity)
            DiscordService.Start();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        LogManager.Shutdown();
    }

    internal static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>();

        builder.WithInterFont();
        builder.UsePlatformDetect();

#if DEBUG
        builder.LogToTrace();
#endif

        builder.LogToNLog(LogEventLevel.Error);

        return builder;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var logger = LogManager.GetCurrentClassLogger();

        if (e.ExceptionObject is Exception exception)
            logger.Log(NLog.LogLevel.Fatal, exception.ToString());
    }
}