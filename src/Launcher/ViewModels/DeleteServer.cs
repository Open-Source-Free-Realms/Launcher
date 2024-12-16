using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Helpers;
using Launcher.Models;

namespace Launcher.ViewModels;

public partial class DeleteServer : Popup
{
    [ObservableProperty]
    private ServerInfo info;

    public DeleteServer(ServerInfo info)
    {
        Info = info;

        View = new Views.DeleteServer()
        {
            DataContext = this
        };
    }

    public override Task<bool> ProcessAsync()
    {
        ProgressDescription = App.GetText("Text.Delete_Server.Loading");

        return Task.Run(OnDeleteServer);
    }

    private bool OnDeleteServer()
    {
        try
        {
            ForceDeleteDirectory(Info.SavePath);
        }
        catch (Exception ex)
        {
            UIThreadHelper.Invoke(() =>
            {
                App.AddNotification($"""
                                     An exception was thrown while deleting server.
                                     Exception: {ex}
                                     """, true);
            });

            return true;
        }

        Settings.Instance.ServerInfoList.Remove(Info);

        Settings.Instance.Save();

        return true;
    }

    private void ForceDeleteDirectory(string path)
    {
        var directory = new DirectoryInfo(path)
        {
            Attributes = FileAttributes.Normal
        };

        // Clear all file/directory attributes.
        foreach (var info in directory.GetFileSystemInfos("*", SearchOption.AllDirectories))
            info.Attributes = FileAttributes.Normal;

        directory.Delete(true);
    }
}