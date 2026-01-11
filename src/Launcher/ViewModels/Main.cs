using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Launcher.Helpers;
using Launcher.Models;
using Launcher.Services;

using NLog;

using NuGet.Versioning;

namespace Launcher.ViewModels;

public partial class Main : ObservableObject
{
    [ObservableProperty]
    private Popup? popup;

    [ObservableProperty]
    private Server? activeServer;

    [ObservableProperty]
    private string message = string.Empty;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private SemanticVersion version = App.CurrentVersion;

    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public AvaloniaList<Server> Servers { get; } = [];
    public AvaloniaList<Notification> Notifications { get; } = [];

    public Main()
    {
#if DESIGNMODE
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            Servers.Clear();
            var demoServer = new Server();
            Servers.Add(demoServer);
            ActiveServer = demoServer;
        }
#endif

        // Subscribe to changes in the server list from settings to keep the UI in sync.
        Settings.Instance.ServerInfoList.CollectionChanged += ServerInfoList_CollectionChanged;
        Settings.Instance.DiscordActivityChanged += (_, _) => UpdateDiscordActivity();
    }

    private void ServerInfoList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex != -1)
        {
            var serverInfo = Settings.Instance.ServerInfoList[e.NewStartingIndex];

            Servers.Add(new Server(serverInfo, this));
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldStartingIndex != -1)
        {
            Servers.RemoveAt(e.OldStartingIndex);
            // If ActiveServer was removed, set to null
            if (ActiveServer != null && !Servers.Contains(ActiveServer))
                ActiveServer = null;
        }
    }

    public void OnLoad()
    {
        foreach (var serverInfo in Settings.Instance.ServerInfoList)
        {
            Servers.Add(new Server(serverInfo, this));
        }
        UpdateDiscordActivity();
    }

    public void UpdateDiscordActivity()
    {
        if (!Settings.Instance.DiscordActivity)
            return;

        var serversPlaying = Servers.Where(x => x.Process is not null).Select(x => x.Info.Name);
        var playingOn = string.Join(", ", serversPlaying);

        var details = string.IsNullOrEmpty(playingOn)
            ? App.GetText("Text.Discord.Idle")
            : App.GetText("Text.Discord.Playing");

        DiscordService.UpdateActivity(details, playingOn);
    }

    [RelayCommand]
    public Task CheckForUpdates() => App.CheckForUpdatesAsync();

    [RelayCommand]
    public void ShowSettings() => App.ShowSettings();

    [RelayCommand]
    public void AddServer() => App.ShowPopup(new AddServer());

    [RelayCommand]
    public async Task OpenLogs()
    {
        try
        {
            if (!Directory.Exists(Constants.LogsDirectory))
            {
                App.AddNotification("Logs directory does not exist.", true);
                return;
            }

            // Platform-specific logic to open a folder
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo.FileName = "explorer.exe";
                startInfo.Arguments = Constants.LogsDirectory;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                startInfo.FileName = "open";
                startInfo.Arguments = Constants.LogsDirectory;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                startInfo.FileName = "xdg-open";
                startInfo.Arguments = Constants.LogsDirectory;
            }
            else
            {
                App.AddNotification("Opening the logs folder is not supported on this operating system.", true);
                return;
            }

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error opening logs directory");
            App.AddNotification($"Failed to open logs directory. Error: {ex.Message}", true);
        }
    }

    [RelayCommand]
    public void DeleteServer()
    {
        if (ActiveServer == null)
            return;

        if (ActiveServer.IsDownloading)
        {
            App.AddNotification("Cannot delete server while download is in progress.", true);
            return;
        }

        // Show a confirmation dialog before deleting
        App.ShowPopup(new DeleteServer(ActiveServer.Info));
    }

    public void OnReceiveNotification(Notification notification)
    {
        // Limit the number of visible notifications
        if (Notifications.Count >= 3)
            Notifications.RemoveAt(0);

        Notifications.Add(notification);

        // Wait for a few seconds before removing the notification
        Task.Run(async () =>
        {
            await Task.Delay(3000);

            Notifications.Remove(notification);
        });
    }
}