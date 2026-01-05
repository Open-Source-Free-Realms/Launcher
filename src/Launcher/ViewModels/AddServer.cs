using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Extensions;
using Launcher.Helpers;
using Launcher.Models;
using NLog;
using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Launcher.ViewModels;

public partial class AddServer : Popup
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    [Required]
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(AddServer), nameof(ValidateServerUrl))]
    private string serverUrl = string.Empty;

    public IAsyncRelayCommand AddServerCommand { get; }
    public ICommand CancelAddServerCommand { get; }

    public AddServer()
    {
        AddServerCommand = new AsyncRelayCommand(OnAddServer);
        CancelAddServerCommand = new RelayCommand(OnAddServerCancel);

        View = new Views.AddServer
        {
            DataContext = this
        };
    }
    private Task OnAddServer() => App.ProcessPopupAsync();

    private void OnAddServerCancel() => App.CancelPopup();

    public static ValidationResult? ValidateServerUrl(string serverUrl, ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            return new ValidationResult(App.GetText("Text.Add_Server.InvalidServerUrl1", "<empty>"));

        // Remove leading/trailing whitespace to handle accidental spaces from copy-pasting.
        serverUrl = serverUrl.Trim();

        // Ensure the URL is a valid, absolute URI.
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri))
            return new ValidationResult(App.GetText("Text.Add_Server.InvalidServerUrl1", serverUrl));

        // Ensure the URL scheme is either HTTP or HTTPS.
        if (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps)
            return new ValidationResult(App.GetText("Text.Add_Server.InvalidServerUrl2", serverUrl));

        return ValidationResult.Success;
    }

    public override Task<bool> ProcessAsync()
    {
        ProgressDescription = App.GetText("Text.Add_Server.Loading");
        return OnAddServerAsync();
    }

    private async Task<bool> OnAddServerAsync()
    {
        try
        {
            ServerUrl = ServerUrl.Trim();

            // Fetch the server manifest from the provided URL.
            var result = await HttpHelper.GetServerManifestAsync(ServerUrl).ConfigureAwait(false);

            if (!result.Success || result.ServerManifest is null)
            {
                await App.AddNotification(result.Error, true);
                return false;
            }

            var serverManifest = result.ServerManifest;

            // Validate the manifest data.
            if (string.IsNullOrEmpty(serverManifest.Name))
                return await NotifyAndReturnFalse("Server name is missing in manifest.");

            // Create a unique local directory for the server's files.
            if (!TryCreateSavePath(serverManifest.Name, out var savePath))
                return await NotifyAndReturnFalse("Failed to create a save path for the server.");

            // If all checks pass, create the ServerInfo object and save it.
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
            _logger.Error(ex, "An exception occurred while adding a server.");
            await App.AddNotification($"An exception occurred: {ex.Message}", true);
            return false;
        }
    }

    private static async Task<bool> NotifyAndReturnFalse(string message)
    {
        await App.AddNotification(message, true);
        return false;
    }

    private static bool TryCreateSavePath(string name, out string path)
    {
        path = string.Empty;
        try
        {
            // Sanitize the server name to be a valid directory name.
            var validName = name.ToValidDirectoryName();
            var basePath = Path.Combine(Constants.SavePath, Constants.ServersDirectory);
            Directory.CreateDirectory(basePath);

            int counter = 1;
            string currentName = validName;

            // Loop until a unique directory name is found.
            while (true)
            {
                var candidatePath = Path.Combine(basePath, currentName);
                if (!Directory.Exists(candidatePath))
                {
                    // Found a unique name, create the directory.
                    Directory.CreateDirectory(candidatePath);
                    path = candidatePath;
                    return true;
                }
                // If the directory exists, append a counter and try again.
                currentName = $"{validName}_{counter++}";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create save path.");
            return false;
        }
    }
}