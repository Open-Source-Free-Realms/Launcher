using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Launcher.Models;
using Launcher.ViewModels;
using NLog;
using NuGet.Versioning;
using System;
using System.Reflection;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Launcher;

public partial class App : Application
{
    private readonly Logger _logger;
    private Main? _main;

    private const string GitHubRepoUrl = "https://github.com/Open-Source-Free-Realms/Launcher";
    private static readonly UpdateManager _updateManager = new(new GithubSource(GitHubRepoUrl, null, false));

    // FIXED: Reads the Assembly Version (from the build script) instead of defaulting to 0.0.0
    public static SemanticVersion CurrentVersion
    {
        get
        {
            if (_updateManager.CurrentVersion != null)
                return _updateManager.CurrentVersion;

            var asmVersion = Assembly.GetExecutingAssembly().GetName().Version;
            return asmVersion != null 
                ? new SemanticVersion(asmVersion.Major, asmVersion.Minor, asmVersion.Build) 
                : new SemanticVersion(0, 0, 0);
        }
    }

    public App()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime applicationLifetime)
            return;

        var main = new Views.Main();
        _main = main.ViewModel;

        applicationLifetime.MainWindow = main;

        main.Show();

        base.OnFrameworkInitializationCompleted();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public static async Task CheckForUpdatesAsync()
    {
        if (Current is not App app || app._main is null)
            return;

        try
        {
            if (_updateManager.IsInstalled)
            {
                app._main.IsRefreshing = true;
                app._main.Message = GetText("Text.Main.CheckingForUpdates");

                var updateInfo = await _updateManager.CheckForUpdatesAsync().ConfigureAwait(false);

                if (updateInfo is null)
                {
                    app._main.Message = GetText("Text.Main.NoUpdatesFound");

                    await Task.Delay(500);

                    app._main.Message = string.Empty;
                }
                else
                {
                    await _updateManager.DownloadUpdatesAsync(updateInfo, p =>
                    {
                        app._main.Message = GetText("Text.Main.Downloading", updateInfo.TargetFullRelease.Version, p);
                    });

                    app._main.Message = GetText("Text.Main.Relaunching");

                    await Task.Delay(500);

                    _updateManager.ApplyUpdatesAndRestart(updateInfo);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            app._logger.Error(ex, "Error checking for updates");
            await AddNotification(GetText("Text.Main.UpdateError"), true);
        }
        finally
        {
            if (app._main != null)
                app._main.IsRefreshing = false;
        }
    }

    public static string GetText(string key, params object?[] args)
    {
        if (Current is not App)
            return key;

        if (Current.FindResource(key) is not string text)
            return $"#{key}";

        return string.Format(text, args);
    }

    public static async Task AddNotification(string message, bool isError = false)
    {
        if (Current is not App app || app._main is null)
            return;

        var notice = new Notification
        {
            IsError = isError,
            Message = message
        };

        app._logger.Log(isError ? LogLevel.Error : LogLevel.Info, message);
        await app._main.OnReceiveNotification(notice);
    }

    public static void ShowSettings()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            var dialog = new Views.Settings();
            dialog.ShowDialog(desktop.MainWindow);
        }
    }

    public static void ClearServerSelection()
    {
        if (Current is not App app || app._main is null)
            return;

        app._main.ActiveServer = null;
    }

    public static async Task ShowPopupAsync(Popup popup, bool process = false)
    {
        if (Current is not App app || app._main is null)
            return;

        if (app._main.Popup?.InProgress ?? false)
            return;

        app._main.Popup = popup;

        if (process)
            await ProcessPopupAsync();
    }

    public static async Task ProcessPopupAsync()
    {
        if (Current is not App app || app._main is null || app._main.Popup is null)
            return;

        if (!app._main.Popup.Validate())
            return;

        app._main.Popup.InProgress = true;

        var task = app._main.Popup.ProcessAsync();

        if (task is null)
        {
            app._main.Popup = null;
            return;
        }

        var finished = await task.ConfigureAwait(false);

        if (finished)
            app._main.Popup = null;
        else
            app._main.Popup.InProgress = false;
    }

    public static void CancelPopup()
    {
        if (Current is not App app)
            return;

        if (app._main is null)
            return;

        if (app._main.Popup?.InProgress ?? true)
            return;

        app._main.Popup = null;
    }
}