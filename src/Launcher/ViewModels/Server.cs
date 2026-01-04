using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Downloader;

using HashDepot;

using Launcher.Helpers;
using Launcher.Models;

using NLog;

namespace Launcher.ViewModels;

public partial class Server : ObservableObject
{
    private readonly Main _main = null!;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

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
    private Server? activeServer;

    [ObservableProperty]
    private bool isRefreshing = false;

    [ObservableProperty]
    private Process? process;

    [ObservableProperty]
    private IBrush? serverStatusFill;

    [ObservableProperty]
    private bool isDownloading = false;

    [ObservableProperty]
    private int filesDownloaded;

    [ObservableProperty]
    private int totalFilesToDownload;

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
    public async Task<bool> OnShowAsync()
    {
        if (!await RefreshServerInfoAsync().ConfigureAwait(false))
            return false;

        await RefreshServerStatusAsync().ConfigureAwait(false);
        return true;
    }

    public void ClientProcessExited(object? sender, EventArgs e)
    {
        Process?.Dispose();
        Process = null;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task RefreshServerStatusAsync()
    {
        await UIThreadHelper.InvokeAsync(() =>
        {
            Status = App.GetText("Text.ServerStatus.Refreshing");
            ServerStatusFill = new SolidColorBrush(Color.FromRgb(204, 204, 0));
            IsRefreshing = true;
            return Task.CompletedTask;
        });

        try
        {
            var serverStatus = await ServerStatusHelper.GetAsync(Info.LoginServer).ConfigureAwait(false);
            IsOnline = serverStatus.IsOnline;

            await UIThreadHelper.InvokeAsync(() =>
            {
                if (serverStatus.IsOnline)
                {
                    Status = App.GetText(serverStatus.IsLocked
                        ? "Text.ServerStatus.Locked"
                        : "Text.ServerStatus.Online");

                    OnlinePlayers = serverStatus.OnlinePlayers;
                    ServerStatusFill = new SolidColorBrush(
                        serverStatus.IsLocked
                            ? Color.FromRgb(242, 63, 67)
                            : Color.FromRgb(35, 165, 90));
                }
                else
                {
                    Status = App.GetText("Text.ServerStatus.Offline");
                    OnlinePlayers = 0;
                    ServerStatusFill = new SolidColorBrush(Color.FromRgb(242, 63, 67));
                }
                return Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Error refreshing server status for: {Info.Name}.");
            await App.AddNotification($"Failed to refresh server status: {ex.Message}.", true);
        }
        finally
        {
            await UIThreadHelper.InvokeAsync(() =>
            {
                IsRefreshing = false;
                return Task.CompletedTask;
            });
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task PlayAsync()
    {
        if (Process != null)
        {
            await App.AddNotification("Unable to launch, the game is already open.", true);
            _logger.Warn($"Unable to launch, the game is already open for server: {Info.Name}.");
            return;
        }

        var clientManifest = await GetClientManifestAsync().ConfigureAwait(false);
        if (clientManifest is null)
            return;

        StatusMessage = App.GetText("Text.Server.VerifyClientFiles");

        if (!await VerifyClientFilesAsync(clientManifest).ConfigureAwait(false))
        {
            await App.AddNotification("Failed to verify client files, please try again.", true);
            _logger.Warn($"Failed to verify client files for server: {Info.Name}.");
            StatusMessage = string.Empty;
            return;
        }

        if (!IsOnline)
        {
            StatusMessage = string.Empty;
            await App.AddNotification("Cannot login: The server is offline.", true);
            return;
        }

        // All checks passed, show the login popup
        await UIThreadHelper.InvokeAsync(async () =>
        {
            StatusMessage = string.Empty;
            await App.ShowPopupAsync(new Login(this));
        });
    }

    [RelayCommand]
    public async Task OpenClientFolder()
    {
        try
        {
            string folderPath = Path.Combine(Constants.SavePath, Info.SavePath);

            if (!Directory.Exists(folderPath))
            {
                await App.AddNotification($"The client folder does not exist: {folderPath}.", true);
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
                startInfo.Arguments = folderPath;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                startInfo.FileName = "open";
                startInfo.Arguments = folderPath;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                startInfo.FileName = "xdg-open";
                startInfo.Arguments = folderPath;
            }
            else
            {
                await App.AddNotification("Opening the client folder is not supported on this operating system.", true);
                return;
            }

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error opening client folder directory.");
            await App.AddNotification($"Failed to open client folder directory. Error: {ex.Message}.", true);
        }
    }
    private async Task<bool> RefreshServerInfoAsync()
    {
        try
        {
            var result = await HttpHelper.GetServerManifestAsync(Info.Url).ConfigureAwait(false);
            if (!result.Success || result.ServerManifest is null)
            {
                await UIThreadHelper.InvokeAsync(async () =>
                {
                    await App.AddNotification(result.Error, true);
                    _logger.Error($"Failed to get server manifest for: {Info.Url}: {result.Error}.");
                });
                return false;
            }
            var serverManifest = result.ServerManifest;

            // Update properties on the UI thread
            await UIThreadHelper.InvokeAsync(() =>
            {
                Info.Name = serverManifest.Name;
                Info.Description = serverManifest.Description;
                Info.LoginServer = serverManifest.LoginServer;
                Info.LoginApiUrl = serverManifest.LoginApiUrl;
                return Task.CompletedTask;
            });

            return true;
        }
        catch (Exception ex)
        {
            await UIThreadHelper.InvokeAsync(async () =>
            {
                await App.AddNotification($"An exception was thrown while getting server info. Exception: {ex.Message}.", true);
                _logger.Error(ex, $"An exception was thrown while getting server info for: {Info.Url}.");
            });
        }
        return false;
    }

    private async Task<ClientManifest?> GetClientManifestAsync()
    {
        try
        {
            var result = await HttpHelper.GetClientManifestAsync(Info.Url).ConfigureAwait(false);
            if (!result.Success || result.ClientManifest is null)
            {
                await UIThreadHelper.InvokeAsync(async () =>
                {
                    await App.AddNotification(result.Error, true);
                    _logger.Error($"Failed to get client manifest for: {Info.Url}: {result.Error}.");
                });
                return null;
            }
            return result.ClientManifest;
        }
        catch (Exception ex)
        {
            await App.AddNotification($"An exception was thrown while getting client info. Exception: {ex.Message}.", true);
            _logger.Error(ex, $"An exception was thrown while getting client info for: {Info.Url}.");
        }
        return null;
    }

    private async Task<bool> VerifyClientFilesAsync(ClientManifest clientManifest)
    {
        _logger.Info($"Starting verifying client files for: {Info.Name}.");
        var filesToDownload = GetFilesToDownloadRecursively(clientManifest.RootFolder).ToList();

        TotalFilesToDownload = filesToDownload.Count;
        FilesDownloaded = 0;

        if (TotalFilesToDownload == 0)
        {
            _logger.Info("All client files are up to date.");
            return true;
        }

        int success = 1;
        int filesDownloadedCount = 0;
        var failedFiles = new ConcurrentBag<string>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(2, Settings.Instance.DownloadThreads)
        };

        IsDownloading = true;

        try
        {
            // Choose between parallel and sequential download based on settings
            if (Settings.Instance.ParallelDownload)
            {
                await Parallel.ForEachAsync(filesToDownload, parallelOptions, async (file, ct) =>
                {
                    if (!await DownloadFileAsync(file.Path, file.FileName, failedFiles).ConfigureAwait(false))
                    {
                        Interlocked.Exchange(ref success, 0);
                    }
                    int currentCount = Interlocked.Increment(ref filesDownloadedCount);
                    await UIThreadHelper.InvokeAsync(() =>
                    {
                        FilesDownloaded = currentCount;
                        StatusMessage = App.GetText("Text.Server.PreparingGameFiles", FilesDownloaded, TotalFilesToDownload);
                        return Task.CompletedTask;
                    });
                });
            }
            else
            {
                foreach (var file in filesToDownload)
                {
                    if (!await DownloadFileAsync(file.Path, file.FileName, failedFiles).ConfigureAwait(false))
                    {
                        success = 0;
                    }
                    filesDownloadedCount++;
                    await UIThreadHelper.InvokeAsync(() =>
                    {
                        FilesDownloaded = filesDownloadedCount;
                        StatusMessage = App.GetText("Text.Server.PreparingGameFiles", FilesDownloaded, TotalFilesToDownload);
                        return Task.CompletedTask;
                    });
                }
            }
        }
        finally
        {
            IsDownloading = false;
        }

        // Report any files that failed to download
        if (!failedFiles.IsEmpty)
        {
            var message = new StringBuilder();
            message.AppendLine($"Failed to download: {failedFiles.Count} file(s):");
            message.AppendLine(string.Join("\n", failedFiles.Take(10)));

            if (failedFiles.Count > 10)
                message.AppendLine($"...And {failedFiles.Count - 10} more.");

            await App.AddNotification(message.ToString(), true);
        }

        _logger.Info($"Finished verifying client files for: {Info.Name}.");
        return success == 1;
    }

    private async Task<bool> DownloadFileAsync(string path, string fileName, ConcurrentBag<string> failedFiles)
    {
        var downloadFilePath = Path.Combine(path, fileName);
        try
        {
            var clientFileUri = UriHelper.JoinUriPaths(Info.Url, "client", path, fileName);
            var fileDirectory = Path.Combine(Constants.SavePath, Info.SavePath, "Client", path);
            var filePath = Path.Combine(fileDirectory, fileName);

            using var downloadService = new DownloadService(new DownloadConfiguration
            {
                RequestConfiguration =
                {
                    UserAgent = HttpHelper.UserAgent
                }
            });

            Directory.CreateDirectory(fileDirectory);

            await using var fileStream = await downloadService.DownloadFileTaskAsync(clientFileUri).ConfigureAwait(false);
            if (fileStream is null || fileStream.Length == 0)
            {
                _logger.Error($"Failed to get client file or received empty stream: {downloadFilePath}.");
                failedFiles.Add(downloadFilePath);
                return false;
            }

            await using var writeStream = File.Create(filePath);
            await fileStream.CopyToAsync(writeStream).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Error downloading: {downloadFilePath}.");
            failedFiles.Add(downloadFilePath);
            return false;
        }
    }

    private IEnumerable<(string Path, string FileName)> GetFilesToDownloadRecursively(ClientFolder clientFolder, string path = "")
    {
        // Recurse into subfolders
        foreach (var folder in clientFolder.Folders)
        {
            var folderPath = Path.Combine(path, folder.Name);
            foreach (var fileToDownload in GetFilesToDownloadRecursively(folder, folderPath))
                yield return fileToDownload;
        }

        // Check files in the current folder
        foreach (var file in clientFolder.Files)
        {
            var fileDirectory = Path.Combine(Constants.SavePath, Info.SavePath, "Client", path);
            var filePath = Path.Combine(fileDirectory, file.Name);

            if (File.Exists(filePath))
            {
                try
                {
                    using var readStream = File.OpenRead(filePath);
                    // First, check if file size matches. This is a quick check before hashing.
                    if (file.Size == readStream.Length)
                    {
                        var hash = XXHash.Hash64(readStream);
                        // If hash also matches, the file is valid.
                        if (file.Hash == hash)
                            continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Could not verify hash for file: {filePath}. Will re-download.");
                }
            }

            // If file doesn't exist, or size/hash mismatch, yield it for download.
            yield return (path, file.Name);
        }
    }

    partial void OnProcessChanged(Process? value)
    {
        _main.UpdateDiscordActivity();
    }
}