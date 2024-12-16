using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Launcher.Models;
using Launcher.ViewModels;
using NLog;
using NuGet.Versioning;
using Velopack;
using Velopack.Sources;

namespace Launcher;

public partial class App : Application
{
    private ResourceDictionary? _activeLocale;

    private readonly Logger _logger;

    private Main? _main;

    private const string GitHubRepoUrl = "https://github.com/Open-Source-Free-Realms/Launcher";
    private static readonly UpdateManager _updateManager = new(new GithubSource(GitHubRepoUrl, null, false));

    public static SemanticVersion CurrentVersion => _updateManager.CurrentVersion ?? new SemanticVersion(0, 0, 0);

    public App()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        SetLocale(Settings.Instance.Locale);

        Settings.Instance.LocaleChanged += (s, e) =>
        {
            SetLocale(Settings.Instance.Locale);
        };
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime applicationLifetime)
            return;

        var settings = Settings.Instance;

        var main = new Views.Main();

        await Task.Delay(500);

#if RELEASE
        if (_updateManager.IsInstalled)
        {

            main.ViewModel.Message = GetText("Text.Main.CheckForUpdates");

            var updateInfo = await _updateManager.CheckForUpdatesAsync();

            if (updateInfo is not null)
            {
                await _updateManager.DownloadUpdatesAsync(updateInfo, (p) =>
                {
                    main.ViewModel.Message = GetText("Text.Main.DownloadProgress", updateInfo.TargetFullRelease.Version, p);
                });

                main.ViewModel.Message = GetText("Text.Main.RestartLauncher");

                await Task.Delay(500);

                _updateManager.ApplyUpdatesAndRestart(updateInfo);

                return;
            }
        }
#endif

        _main = main.ViewModel;

        applicationLifetime.MainWindow = main;

        main.Show();

        base.OnFrameworkInitializationCompleted();

        main.ViewModel.Message = GetText("Text.Main.LauncherUpToDate");
    }

    public static void SetLocale(LocaleType value)
    {
        if (Current is not App app)
            return;

        if (!app.Resources.TryGetValue(value.ToString(), out var localeResource) ||
            localeResource is not ResourceDictionary targetLocale)
        {
            app._logger.Error("Invalid locale. {locale}", value);

            return;
        }

        if (app._activeLocale is not null)
            app.Resources.MergedDictionaries.Remove(app._activeLocale);

        app.Resources.MergedDictionaries.Add(targetLocale);

        app._activeLocale = targetLocale;
    }

    public static string GetText(string key, params object?[] args)
    {
        if (Current is not App app)
            return key;

        var text = Current.FindResource(key) as string;

        if (text is null)
            return $"#{key}";

        return string.Format(text, args);
    }

    public static void AddNotification(string message, bool isError = false)
    {
        if (Current is not App app || app._main is null)
            return;

        var notice = new Notification()
        {
            IsError = isError,
            Message = message
        };

        app._logger.Log(isError ? LogLevel.Error : LogLevel.Info, message);

        app._main.OnReceiveNotification(notice);
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

    public static void ShowPopup(Popup popup, bool process = false)
    {
        if (Current is not App app || app._main is null)
            return;

        if (app._main.Popup is not null && app._main.Popup.InProgress)
            return;

        app._main.Popup = popup;

        if (process)
            ProcessPopup();
    }

    public static async void ProcessPopup()
    {
        if (Current is not App app || app._main is null)
            return;

        if (app._main.Popup is null || !app._main.Popup.Validate())
            return;

        app._main.Popup.InProgress = true;

        var task = app._main.Popup.ProcessAsync();

        if (task is null)
        {
            app._main.Popup = null;
            return;
        }

        var finished = await task;

        if (finished)
            app._main.Popup = null;
        else
            app._main.Popup.InProgress = false;
    }

    public static void CancelPopup()
    {
        if (Current is not App app || app._main is null)
            return;

        if (app._main.Popup is null || app._main.Popup.InProgress)
            return;

        app._main.Popup = null;
    }
}