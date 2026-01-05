using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Helpers;
using Launcher.Models;
using NLog;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Launcher.ViewModels;

public partial class DeleteServer : Popup
{
    [ObservableProperty]
    private ServerInfo info;

    public IAsyncRelayCommand DeleteServerCommand { get; }
    public ICommand CancelDeleteServerCommand { get; }

    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public DeleteServer(ServerInfo info)
    {
        Info = info;

        DeleteServerCommand = new AsyncRelayCommand(OnDeleteServer);
        CancelDeleteServerCommand = new RelayCommand(OnDeleteServerCancel);

        View = new Views.DeleteServer
        {
            DataContext = this
        };
    }
    private Task OnDeleteServer() => App.ProcessPopupAsync();

    private void OnDeleteServerCancel() => App.CancelPopup();

    public override Task<bool> ProcessAsync()
    {
        ProgressDescription = App.GetText("Text.Delete_Server.Loading");
        return OnDeleteServerAsync();
    }
    private async Task<bool> OnDeleteServerAsync()
    {
        try
        {
            // Delete the server's directory and all its contents from the file system.
            var serverDirectoryPath = Path.Combine(Constants.SavePath, Info.SavePath);
            await ForceDeleteDirectoryAsync(serverDirectoryPath);
        }
        catch (Exception ex)
        {
            // If file deletion fails, notify the user and log the error.
            _logger.Error(ex, $"Error deleting server directory for: {Info.Name}");
            await App.AddNotification($"Failed to delete server directory: {ex.Message}", true);
            return false;
        }

        await UIThreadHelper.InvokeAsync(() =>
        {
            try
            {
                Settings.Instance.ServerInfoList.Remove(Info);
                Settings.Instance.Save();
            }
            catch (Exception ex)
            {
                // This is a secondary failure, but we should still log it.
                _logger.Error(ex, "Error removing server info from settings after directory deletion.");
            }
            return Task.CompletedTask;
        });

        return true;
    }

    private async Task ForceDeleteDirectoryAsync(string path)
    {
        if (!Directory.Exists(path))
            return;

        await Task.Run(() =>
        {
            try
            {
                var directoryInfo = new DirectoryInfo(path);

                foreach (var info in directoryInfo.GetFileSystemInfos("*", SearchOption.AllDirectories))
                {
                    info.Attributes = FileAttributes.Normal;
                }

                // Delete the directory and all its contents.
                directoryInfo.Delete(true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to forcefully delete directory: {path}");
                throw; // Re-throw the exception to be caught by the calling method.
            }
        });
    }
}