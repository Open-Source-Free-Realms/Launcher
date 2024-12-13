using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashDepot;
using Launcher.Helpers;
using Launcher.Models;

namespace Launcher.ViewModels;

public partial class Server : ObservableObject
{
    private readonly Main _main = null!;

    [ObservableProperty]
    private ServerInfo info = null!;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string status = App.GetText("Text.ServerStatus.Offline");

    [ObservableProperty]
    private int onlinePlayers;

    [ObservableProperty]
    private bool isOnline;

    [ObservableProperty]
    private bool isRefreshing = false;

    [ObservableProperty]
    private Process? process;

    [ObservableProperty]
    public IBrush? serverStatusFill;

    public Server()
    {
#if DESIGNMODE
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            Info = new ServerInfo
            {
                Url = "https://example.com",

                Name = "Name",
                Description = "Description",

                SavePath = "Name",

                LoginServer = "127.0.0.1:20042",
                LoginApiUrl = "https://example.com"
            };
        }
#endif
    }

    public Server(ServerInfo info, Main main)
    {
        Info = info;
        _main = main;
    }

    public async Task<bool> OnShow()
    {
        if (!await RefreshServerInfoAsync())
            return false;

        await RefreshServerStatusAsync();

        return true;
    }

    public void ClientProcessExited(object? sender, EventArgs e)
    {
        Process = null;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task RefreshServerStatusAsync()
    {
        IsRefreshing = true;

        var serverStatus = await ServerStatusHelper.GetAsync(Info.LoginServer);

        IsOnline = serverStatus.IsOnline;

        if (serverStatus.IsOnline)
        {
            Status = App.GetText(serverStatus.IsLocked
                ? "Text.ServerStatus.Locked"
                : "Text.ServerStatus.Online");

            OnlinePlayers = serverStatus.OnlinePlayers;

            ServerStatusFill = serverStatus.IsLocked
                ? new SolidColorBrush(Color.FromRgb(242, 63, 67))
                : new SolidColorBrush(Color.FromRgb(35, 165, 90));
        }
        else
        {
            Status = App.GetText("Text.ServerStatus.Offline");

            OnlinePlayers = 0;

            ServerStatusFill = new SolidColorBrush(Color.FromRgb(242, 63, 67));
        }

        IsRefreshing = false;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task PlayAsync()
    {
        if (Process is not null)
        {
            App.AddNotification("Unable to play, the game is already open");

            return;
        }

        var clientManifest = await GetClientManifestAsync();

        if (clientManifest is null)
            return;

        StatusMessage = App.GetText("Text.Server.VerifyClientFiles");

        if (!await VerifyClientFilesAsync(clientManifest))
            return;

        StatusMessage = string.Empty;

        App.ShowPopup(new Login(this));
    }

    [RelayCommand()]
    public void OpenFolder()
    {
        Process.Start(new ProcessStartInfo()
        {
            Verb = "open",
            UseShellExecute = true,
            FileName = Path.Combine(Environment.CurrentDirectory, Info.SavePath)
        });
    }

    private async Task<bool> RefreshServerInfoAsync()
    {
        try
        {
            var result = await HttpHelper.GetServerManifestAsync(Info.Url);

            if (!result.Success || result.ServerManifest is null)
            {
                UIThreadHelper.Invoke(() =>
                {
                    App.AddNotification(result.Error, true);
                });

                return false;
            }

            var serverManifest = result.ServerManifest;

            Info.Name = serverManifest.Name;
            Info.Description = serverManifest.Description;

            Info.LoginServer = serverManifest.LoginServer;
            Info.LoginApiUrl = serverManifest.LoginApiUrl;

            return true;
        }
        catch (Exception ex)
        {
            UIThreadHelper.Invoke(() =>
            {
                App.AddNotification($"""
                                     An exception was thrown while getting server info.
                                     Exception: {ex}
                                     """, true);
            });
        }

        return false;
    }

    private async Task<ClientManifest?> GetClientManifestAsync()
    {
        try
        {
            var result = await HttpHelper.GetClientManifestAsync(Info.Url);

            if (!result.Success || result.ClientManifest is null)
            {
                UIThreadHelper.Invoke(() =>
                {
                    App.AddNotification(result.Error, true);
                });

                return null;
            }

            return result.ClientManifest;
        }
        catch (Exception ex)
        {
            UIThreadHelper.Invoke(() =>
            {
                App.AddNotification($"""
                                     An exception was thrown while getting client info.
                                     Exception: {ex}
                                     """, true);
            });
        }

        return null;
    }

    private async Task<bool> VerifyClientFilesAsync(ClientManifest clientManifest)
    {
        var filesToDownload = GetFilesToDownloadRecursively(clientManifest.RootFolder);

        var success = true;

        await Parallel.ForEachAsync(filesToDownload, async (file, ct) =>
        {
            StatusMessage = App.GetText("Text.Server.DownloadingFile", Path.Combine(file.Path, file.FileName));

            var result = await HttpHelper.GetClientFileAsync(Info.Url, file.Path, file.FileName);

            if (!result.Success || result.Stream is null)
            {
                UIThreadHelper.Invoke(() =>
                {
                    App.AddNotification(result.Error, true);
                });

                success = false;

                return;
            }

            var fileDirectory = Path.Combine(Info.SavePath, "Client", file.Path);
            var filePath = Path.Combine(fileDirectory, file.FileName);

            if (!Directory.Exists(fileDirectory))
                Directory.CreateDirectory(fileDirectory);

            using var writeStream = File.Create(filePath);

            await result.Stream.CopyToAsync(writeStream);
        });

        return success;
    }

    private IEnumerable<(string Path, string FileName)> GetFilesToDownloadRecursively(ClientFolder clientFolder, string path = "")
    {
        foreach (var folder in clientFolder.Folders)
        {
            var folderPath = Path.Combine(path, folder.Name);

            foreach (var fileToDownload in GetFilesToDownloadRecursively(folder, folderPath))
                yield return fileToDownload;
        }

        foreach (var file in clientFolder.Files)
        {
            var fileDirectory = Path.Combine(Info.SavePath, "Client", path);
            var filePath = Path.Combine(fileDirectory, file.Name);

            if (File.Exists(filePath))
            {
                using var readStream = File.OpenRead(filePath);

                if (file.Size == readStream.Length)
                {
                    var hash = XXHash.Hash64(readStream);

                    if (file.Hash == hash)
                        continue;
                }
            }

            yield return (path, file.Name);
        }
    }

    partial void OnProcessChanged(Process? value)
    {
        _main.UpdateDiscordActivity();
    }
}