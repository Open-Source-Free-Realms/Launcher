using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downloader;
using HashDepot;
using Launcher.Helpers;
using Launcher.Models;
using Launcher.Services;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.ViewModels;

public partial class Server : ObservableObject
{
    private readonly Main _main = null!;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty] private ServerInfo info = null!;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string status = App.GetText("Text.ServerStatus.Offline");
    [ObservableProperty] private int onlinePlayers;
    [ObservableProperty] private bool isOnline;
    [ObservableProperty] private bool isRefreshing = false;
    [ObservableProperty] private Process? process;
    [ObservableProperty] private IBrush? serverStatusFill;
    [ObservableProperty] private bool isDownloading = false;
    [ObservableProperty] private int filesDownloaded;
    [ObservableProperty] private int totalFilesToDownload;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanPlay))] private bool isGameRunning = false;

    public bool CanPlay => !IsGameRunning && !IsDownloading;
    public bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public Server() { }
    public Server(ServerInfo info, Main main) { Info = info; _main = main; }

    public async Task<bool> OnShowAsync()
    {
        if (!await RefreshServerInfoAsync().ConfigureAwait(false)) return false;
        await RefreshServerStatusAsync().ConfigureAwait(false);
        return true;
    }

    public void ClientProcessExited(object? sender, EventArgs e)
    {
        Process?.Dispose();
        Process = null;
        IsGameRunning = false;
        _main.UpdateDiscordActivity();
    }

    [RelayCommand]
    public async Task OpenConfig()
    {
        var iniPath = Path.Combine(Constants.SavePath, Info.SavePath, "Client", "UserOptions.ini");
        if (!File.Exists(iniPath))
        {
            await App.AddNotification("Config missing. Launch the game once to generate it!", true);
            return;
        }

        var settingsVm = new GraphicsSettings(this);
        var settingsWindow = new Views.GraphicsSettings { DataContext = settingsVm };

        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && 
            desktop.MainWindow != null)
        {
            await settingsWindow.ShowDialog(desktop.MainWindow);
        }
        else
        {
            settingsWindow.Show();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task RefreshServerStatusAsync()
    {
        await UIThreadHelper.InvokeAsync(() => { Status = App.GetText("Text.ServerStatus.Refreshing"); IsRefreshing = true; return Task.CompletedTask; });
        try
        {
            var serverStatus = await ServerStatusHelper.GetAsync(Info.LoginServer).ConfigureAwait(false);
            IsOnline = serverStatus.IsOnline;
            await UIThreadHelper.InvokeAsync(() =>
            {
                if (serverStatus.IsOnline)
                {
                    Status = App.GetText(serverStatus.IsLocked ? "Text.ServerStatus.Locked" : "Text.ServerStatus.Online");
                    OnlinePlayers = serverStatus.OnlinePlayers;
                    ServerStatusFill = new SolidColorBrush(serverStatus.IsLocked ? Color.FromRgb(242, 63, 67) : Color.FromRgb(35, 165, 90));
                }
                else { Status = App.GetText("Text.ServerStatus.Offline"); OnlinePlayers = 0; ServerStatusFill = new SolidColorBrush(Color.FromRgb(242, 63, 67)); }
                return Task.CompletedTask;
            });
        }
        catch { }
        finally { await UIThreadHelper.InvokeAsync(() => { IsRefreshing = false; return Task.CompletedTask; }); }
    }

    [RelayCommand]
    public async Task OpenClientFolder()
    {
        try
        {
            string folderPath = Path.Combine(Constants.SavePath, Info.SavePath);
            if (!Directory.Exists(folderPath)) { await App.AddNotification("Folder missing.", true); return; }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", folderPath) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Fixed: Added quotes and disabled ShellExecute to handle paths with spaces reliably on macOS
                Process.Start(new ProcessStartInfo("open", $"\"{folderPath}\"") { UseShellExecute = false });
            }
            else // Linux
            {
                // Most reliable way on Linux across different File Managers
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{folderPath}\"") { UseShellExecute = false });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open folder");
            await App.AddNotification("Could not open folder.", true);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task PlayAsync()
    {
        if (Process != null)
        {
            if (Process.HasExited) { Process.Dispose(); Process = null; IsGameRunning = false; }
            else { await App.AddNotification("Game is already running.", true); return; }
        }

        var clientManifest = await GetClientManifestAsync().ConfigureAwait(false);
        if (clientManifest is null) return;

        StatusMessage = App.GetText("Text.Server.VerifyClientFiles");
        if (!await VerifyClientFilesAsync(clientManifest).ConfigureAwait(false))
        {
            await App.AddNotification("Verification failed.", true);
            StatusMessage = string.Empty;
            return;
        }

        if (!IsOnline) { StatusMessage = string.Empty; await App.AddNotification("Server is offline.", true); return; }
        IsGameRunning = true;
        StatusMessage = string.Empty;
        await UIThreadHelper.InvokeAsync(async () => { await App.ShowPopupAsync(new Login(this)); });
    }

    private async Task<bool> RefreshServerInfoAsync() { try { var result = await HttpHelper.GetServerManifestAsync(Info.Url).ConfigureAwait(false); if (!result.Success || result.ServerManifest is null) return false; await UIThreadHelper.InvokeAsync(() => { Info.Name = result.ServerManifest.Name; Info.Description = result.ServerManifest.Description; Info.LoginServer = result.ServerManifest.LoginServer; Info.LoginApiUrl = result.ServerManifest.LoginApiUrl; return Task.CompletedTask; }); return true; } catch { return false; } }
    private async Task<ClientManifest?> GetClientManifestAsync() { try { var result = await HttpHelper.GetClientManifestAsync(Info.Url).ConfigureAwait(false); return result.ClientManifest; } catch { return null; } }
    private async Task<bool> VerifyClientFilesAsync(ClientManifest clientManifest) { var files = GetFilesToDownloadRecursively(clientManifest.RootFolder).ToList(); TotalFilesToDownload = files.Count; FilesDownloaded = 0; if (TotalFilesToDownload == 0) return true; IsDownloading = true; try { var bag = new ConcurrentBag<string>(); await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (file, ct) => { await DownloadFileAsync(file.Path, file.FileName, bag).ConfigureAwait(false); await UIThreadHelper.InvokeAsync(() => { FilesDownloaded++; StatusMessage = App.GetText("Text.Server.PreparingGameFiles", FilesDownloaded, TotalFilesToDownload); return Task.CompletedTask; }); }); return bag.IsEmpty; } finally { IsDownloading = false; } }
    private async Task<bool> DownloadFileAsync(string path, string fileName, ConcurrentBag<string> bag) { try { var uri = UriHelper.JoinUriPaths(Info.Url, "client", path, fileName); var dir = Path.Combine(Constants.SavePath, Info.SavePath, "Client", path); Directory.CreateDirectory(dir); var dest = Path.Combine(dir, fileName); using var ds = new DownloadService(new DownloadConfiguration()); await using var s = await ds.DownloadFileTaskAsync(uri).ConfigureAwait(false); await using var f = File.Create(dest); await s.CopyToAsync(f).ConfigureAwait(false); return true; } catch { bag.Add(fileName); return false; } }
    private IEnumerable<(string Path, string FileName)> GetFilesToDownloadRecursively(ClientFolder folder, string path = "") { foreach (var sub in folder.Folders) { foreach (var f in GetFilesToDownloadRecursively(sub, Path.Combine(path, sub.Name))) yield return f; } foreach (var file in folder.Files) { var fPath = Path.Combine(Constants.SavePath, Info.SavePath, "Client", path, file.Name); if (File.Exists(fPath)) { using var s = File.OpenRead(fPath); if (file.Size == s.Length && file.Hash == XXHash.Hash64(s)) continue; } yield return (path, file.Name); } }
    partial void OnProcessChanged(Process? value) => _main.UpdateDiscordActivity();
}