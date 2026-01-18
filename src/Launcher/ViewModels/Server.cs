using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    public Server()
    {
#if DEBUG && DESIGNMODE
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            var faker = new Bogus.Faker();

            Info = new ServerInfo
            {
                Url = "https://example.com",
                Name = $"{faker.Name.FirstName()}'s Server",
                Description = faker.Lorem.Paragraphs(5),
                SavePath = "Name",
                LoginServer = "127.0.0.1:20042",
                WebApiUrl = "https://example.com"
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
        if (!await RefreshServerInfoAsync())
            return false;

        await RefreshServerStatusAsync();

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
        IsRefreshing = true;

        Status = App.GetText("Text.ServerStatus.Refreshing");

        ServerStatusFill = new SolidColorBrush(Color.FromRgb(204, 204, 0));

        try
        {
            var serverStatus = await ServerStatusHelper.GetAsync(Info.LoginServer);

            IsOnline = serverStatus.IsOnline;

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
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error refreshing server status for: '{Name}'.", Info.Name);

            App.AddNotification("Unable to refresh server status.", true);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task PlayAsync()
    {
        if (Process != null)
        {
            App.AddNotification("Unable to launch, the game is already open.", true);

            _logger.Warn("Unable to launch, the game is already open for server: '{Name}'.", Info.Name);

            return;
        }

        var clientManifest = await GetClientManifestAsync();

        if (clientManifest is null)
            return;

        StatusMessage = App.GetText("Text.Server.VerifyClientFiles");

        if (!await VerifyClientFilesAsync(clientManifest))
        {
            StatusMessage = string.Empty;

            return;
        }

        if (!IsOnline)
        {
            StatusMessage = string.Empty;

            App.AddNotification("Unable to login, the server is offline.", true);

            return;
        }

        StatusMessage = string.Empty;

        // All checks passed, show the login popup
        App.ShowPopup(new Login(this));
    }

    [RelayCommand]
    public async Task OpenFolder()
    {
        var folderPath = Path.Combine(Constants.SavePath, Info.SavePath);

        if (!Directory.Exists(folderPath))
            return;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error opening client folder directory.");

            App.AddNotification("Unable to open server directory.", true);
        }
    }

    private async Task<bool> RefreshServerInfoAsync()
    {
        try
        {
            var result = await HttpHelper.GetServerManifestAsync(Info.Url);

            if (!result.Success || result.ServerManifest is null)
            {
                App.AddNotification($"""
                                     Failed to get server info.
                                     {result.Error}
                                     """, true);

                _logger.Error("Failed to get server manifest for: {Url}: {Error}.", Info.Url, result.Error);

                return false;
            }

            var serverManifest = result.ServerManifest;

            Info.Name = serverManifest.Name;
            Info.Description = serverManifest.Description;

            Info.WebApiUrl = serverManifest.WebApiUrl;
            Info.LoginServer = serverManifest.LoginServer;

            Settings.Instance.Save();

            return true;
        }
        catch (Exception ex)
        {
            App.AddNotification("An error occurred while getting server info.", true);

            _logger.Error(ex, "An exception was thrown while getting server info for: {Url}.", Info.Url);
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
                App.AddNotification($"""
                                     Failed to get client info.
                                     {result.Error}
                                     """, true);

                _logger.Error("Failed to get client manifest for: {Url}: {Error}.", Info.Url, result.Error);

                return null;
            }

            return result.ClientManifest;
        }
        catch (Exception ex)
        {
            App.AddNotification("An error occurred while getting client info.", true);

            _logger.Error(ex, "An exception was thrown while getting client info for: {Url}.", Info.Url);
        }

        return null;
    }

    private async Task<bool> VerifyClientFilesAsync(ClientManifest clientManifest)
    {
        _logger.Info("Starting verifying client files for: {Name}.", Info.Name);

        var filesToDownload = await GetFilesToDownloadAsync(clientManifest.RootFolder);

        if (filesToDownload.Count == 0)
        {
            _logger.Info("All client files are up to date.");
            return true;
        }

        IsDownloading = true;

        var failedFiles = new ConcurrentBag<string>();

        try
        {
            var filesDownloaded = 0;

            // Choose between parallel and sequential download based on settings
            if (Settings.Instance.ParallelDownload)
            {
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(2, Settings.Instance.DownloadThreads)
                };

                await Parallel.ForEachAsync(filesToDownload, parallelOptions, async (file, ct) =>
                {
                    if (!await DownloadFileAsync(file.Path, file.Name))
                        failedFiles.Add(file.Name);

                    filesDownloaded = Interlocked.Increment(ref filesDownloaded);

                    StatusMessage = App.GetText("Text.Server.PreparingGameFiles", filesDownloaded, filesToDownload.Count);
                });
            }
            else
            {
                foreach (var file in filesToDownload)
                {
                    if (!await DownloadFileAsync(file.Path, file.Name))
                        failedFiles.Add(file.Name);

                    filesDownloaded++;

                    StatusMessage = App.GetText("Text.Server.PreparingGameFiles", filesDownloaded, filesToDownload.Count);
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

            message.AppendLine($"Failed to download {failedFiles.Count} file(s):");
            message.AppendLine(string.Join("\n", failedFiles.Take(10)));

            if (failedFiles.Count > 10)
                message.AppendLine($"...And {failedFiles.Count - 10} more.");

            App.AddNotification(message.ToString(), true);
        }

        _logger.Info("Finished verifying client files for: {Name}.", Info.Name);

        return failedFiles.IsEmpty;
    }

    private async Task<bool> DownloadFileAsync(string path, string fileName)
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

            await using var fileStream = await downloadService.DownloadFileTaskAsync(clientFileUri);

            if (fileStream is null || fileStream.Length == 0)
            {
                _logger.Error("Failed to get client file or received empty stream: {Path}.", downloadFilePath);
                return false;
            }

            await using var writeStream = File.Create(filePath);

            await fileStream.CopyToAsync(writeStream);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error downloading: {Path}.", downloadFilePath);

            return false;
        }
    }

    private async Task<List<LocalFile>> GetFilesToDownloadAsync(ClientFolder rootFolder, string path = "")
    {
        var results = new List<LocalFile>();

        // Recurse into subfolders
        foreach (var folder in rootFolder.Folders)
        {
            var folderPath = Path.Combine(path, folder.Name);

            var folderResults = await GetFilesToDownloadAsync(folder, folderPath);

            results.AddRange(folderResults);
        }

        // Check files in the current folder
        foreach (var file in rootFolder.Files)
        {
            var fileDirectory = Path.Combine(Constants.SavePath, Info.SavePath, "Client", path);
            var filePath = Path.Combine(fileDirectory, file.Name);

            if (File.Exists(filePath))
            {
                try
                {
                    await using var readStream = File.OpenRead(filePath);

                    // First, check if file size matches. This is a quick check before hashing.
                    if (file.Size == readStream.Length)
                    {
                        var hash = await Task.Run(() => XXHash.Hash64(readStream));

                        // If hash also matches, the file is valid.
                        if (file.Hash == hash)
                            continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not verify hash for file: {Path}.", filePath);
                }
            }

            // If file doesn't exist, or size/hash mismatch, add it for download.
            results.Add(new LocalFile
            {
                Path = path,
                Name = file.Name
            });
        }

        return results;
    }

    partial void OnProcessChanged(Process? value)
    {
        _main.UpdateDiscordActivity();
    }
}