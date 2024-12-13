using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Extensions;
using Launcher.Helpers;
using Launcher.Models;

namespace Launcher.ViewModels;

public partial class AddServer : Popup
{
    [Required]
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(AddServer), nameof(ValidateServerUrl))]
    private string serverUrl = string.Empty;

    public AddServer()
    {
        View = new Views.AddServer()
        {
            DataContext = this
        };
    }

    public static ValidationResult? ValidateServerUrl(string serverUrl, ValidationContext context)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri))
            return new ValidationResult(App.GetText("Text.Add_Server.InvalidServerUrl1", serverUrl));

        if (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps)
            return new ValidationResult(App.GetText("Text.Add_Server.InvalidServerUrl2", serverUrl));

        return ValidationResult.Success;
    }

    public override Task<bool> ProcessAsync()
    {
        ProgressDescription = App.GetText("Text.Add_Server.Loading");

        return Task.Run(OnAddServer);
    }

    private async Task<bool> OnAddServer()
    {
        try
        {
            var result = await HttpHelper.GetServerManifestAsync(ServerUrl);

            if (!result.Success || result.ServerManifest is null)
            {
                UIThreadHelper.Invoke(() =>
                {
                    App.AddNotification(result.Error, true);
                });

                return false;
            }

            var serverManifest = result.ServerManifest;

            if (!TryCreateSavePath(serverManifest.Name, out var savePath))
            {
                UIThreadHelper.Invoke(() =>
                {
                    App.AddNotification($"""
                                         Failed to create a save path for server.
                                         """, true);
                });

                return false;
            }

            var serverInfo = new ServerInfo
            {
                Url = ServerUrl,

                Name = serverManifest.Name,
                Description = serverManifest.Description,

                LoginServer = serverManifest.LoginServer,
                LoginApiUrl = serverManifest.LoginApiUrl,

                SavePath = savePath
            };

            Settings.Instance.ServerInfoList.Add(serverInfo);

            Settings.Instance.Save();

            return true;
        }
        catch (Exception ex)
        {
            UIThreadHelper.Invoke(() =>
            {
                App.AddNotification($"""
                                     An exception was thrown while getting server manifest.
                                     Exception: {ex}
                                     """, true);
            });
        }

        return false;
    }

    private bool TryCreateSavePath(string name, out string path)
    {
        const string BaseDirectory = "Servers";

        path = string.Empty;

        try
        {
            var validName = name.ToValidDirectoryName();

            var current = validName;

            var i = 1;
            while (Directory.Exists(Path.Combine(BaseDirectory, current)))
                current = $"{validName}_{i++}";

            path = Path.Combine(BaseDirectory, current);

            Directory.CreateDirectory(path);
        }
        catch
        {
            return false;
        }

        return true;
    }
}