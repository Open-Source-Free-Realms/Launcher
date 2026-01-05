using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Helpers;
using Launcher.Models;
using Launcher.Services;
using NLog;
using NuGet.Versioning;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Launcher.ViewModels;

public partial class Main : ObservableObject
{
    [ObservableProperty] private Popup? popup;
    [ObservableProperty] private Server? activeServer;
    [ObservableProperty] private string message = string.Empty;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private SemanticVersion version = App.CurrentVersion;

    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    public AvaloniaList<Server> Servers { get; } = [];
    public AvaloniaList<Notification> Notifications { get; } = [];

    public Main()
    {
        Settings.Instance.ServerInfoList.CollectionChanged += ServerInfoList_CollectionChanged;
        Settings.Instance.DiscordActivityChanged += (_, _) => UpdateDiscordActivity();
    }

    private void ServerInfoList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex != -1)
                Servers.Add(new Server(Settings.Instance.ServerInfoList[e.NewStartingIndex], this));
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldStartingIndex != -1)
            {
                Servers.RemoveAt(e.OldStartingIndex);
                if (ActiveServer != null && !Servers.Contains(ActiveServer)) ActiveServer = null;
            }
        });
    }

    public async void OnLoad()
    {
        bool isUnix = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        if (isUnix && !WineSetupService.IsInstalled())
        {
            var setup = new WineSetup();
            await App.ShowPopupAsync(setup);
            await setup.ProcessAsync();
            App.CancelPopup();
        }

        foreach (var serverInfo in Settings.Instance.ServerInfoList) Servers.Add(new Server(serverInfo, this));
        UpdateDiscordActivity();
    }

    public void UpdateDiscordActivity()
    {
        if (!Settings.Instance.DiscordActivity) return;
        var serversPlaying = Servers.Where(x => x.Process is not null).Select(x => x.Info.Name);
        var playingOn = string.Join(", ", serversPlaying);
        DiscordService.UpdateActivity(string.IsNullOrEmpty(playingOn) ? "Idle" : "Playing", playingOn);
    }

    public async Task OnReceiveNotification(Notification notification)
    {
        await Dispatcher.UIThread.InvokeAsync(() => {
            if (Notifications.Count >= 3) Notifications.RemoveAt(0);
            Notifications.Add(notification);
        });

        await Task.Delay(3000);

        await Dispatcher.UIThread.InvokeAsync(() => Notifications.Remove(notification));
    }

    [RelayCommand] public Task CheckForUpdates() => App.CheckForUpdatesAsync();
    [RelayCommand] public void ShowSettings() => App.ShowSettings();
    [RelayCommand] public Task AddServer() => App.ShowPopupAsync(new AddServer());

    [RelayCommand]
    public async Task OpenLogs()
    {
        if (!Directory.Exists(Constants.LogsDirectory)) return;

        try
        {
            var psi = new ProcessStartInfo { UseShellExecute = true };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi.FileName = "open";
                psi.ArgumentList.Add(Constants.LogsDirectory);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.FileName = "explorer.exe";
                psi.Arguments = Constants.LogsDirectory;
            }
            else // Linux Fix
            {
                psi.FileName = "xdg-open";
                psi.Arguments = $"\"{Constants.LogsDirectory}\"";
                psi.UseShellExecute = false;
            }
            Process.Start(psi);
        }
        catch { await App.AddNotification("Could not open logs folder.", true); }
    }

    [RelayCommand] public async Task DeleteServer()
    {
        if (ActiveServer == null || ActiveServer.IsDownloading) return;
        await App.ShowPopupAsync(new DeleteServer(ActiveServer.Info));
    }
}
