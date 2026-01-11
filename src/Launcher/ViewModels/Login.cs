using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using Launcher.Helpers;
using Launcher.Models;

using NLog;

namespace Launcher.ViewModels;

public partial class Login : Popup
{
    private readonly Server _server;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty]
    private string? warning;

    [Required]
    [ObservableProperty]
    [NotifyDataErrorInfo]
    private string username = string.Empty;

    [Required]
    [ObservableProperty]
    [NotifyDataErrorInfo]
    private string password = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool rememberUsername;

    [ObservableProperty]
    private bool rememberPassword;

    public bool AutoFocusUsername => string.IsNullOrEmpty(Username);
    public bool AutoFocusPassword => !string.IsNullOrEmpty(Username) && string.IsNullOrEmpty(Password);

    public Login(Server server)
    {
        _server = server;

        AddSecureWarning();

        // Load saved credentials based on user's preferences
        RememberUsername = _server.Info.RememberUsername;
        RememberPassword = _server.Info.RememberPassword;
        Username = RememberUsername ? _server.Info.Username ?? string.Empty : string.Empty;
        Password = RememberPassword ? _server.Info.Password ?? string.Empty : string.Empty;

        View = new Views.Login
        {
            DataContext = this
        };
    }

    private void AddSecureWarning()
    {
        if (Uri.TryCreate(_server.Info.LoginApiUrl, UriKind.Absolute, out var loginApiUrl)
            && loginApiUrl.Scheme != Uri.UriSchemeHttps)
        {
            Warning = App.GetText("Text.Login.SecureApiWarning");
        }
    }

    // Handles changes to the "Remember Username" checkbox
    partial void OnRememberUsernameChanged(bool value)
    {
        _server.Info.RememberUsername = value;
        if (!value)
            _server.Info.Username = null;

        Settings.Instance.Save();
    }

    // Handles changes to the "Remember Password" checkbox
    partial void OnRememberPasswordChanged(bool value)
    {
        _server.Info.RememberPassword = value;
        if (!value)
            _server.Info.Password = null;

        Settings.Instance.Save();
    }

    private void SaveRememberedCredentials()
    {
        _server.Info.Username = RememberUsername && !string.IsNullOrEmpty(Username) ? Username : null;
        _server.Info.Password = RememberPassword && !string.IsNullOrEmpty(Password) ? Password : null;
        Settings.Instance.Save();
    }

    public override async Task<bool> ProcessAsync()
    {
        try
        {
            using var httpClient = HttpHelper.CreateHttpClient();
            var loginRequest = new LoginRequest { Username = Username, Password = Password };
            ProgressDescription = App.GetText("Text.Login.Loading");

            // Send login request to the API
            var httpResponse = await httpClient.PostAsJsonAsync(_server.Info.LoginApiUrl, loginRequest);

            if (httpResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                App.AddNotification(App.GetText("Text.Login.Unauthorized"), true);
                Password = string.Empty; // Clear password field on failure
                return false;
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                App.AddNotification($"Failed to login. Http Error: {httpResponse.ReasonPhrase}.", true);
                _logger.Warn("Login failed for server: '{Name}'. API returned status {StatusCode} {Reason}.", _server.Info.Name, httpResponse.StatusCode, httpResponse.ReasonPhrase);
                return false;
            }

            var loginResponse = await httpResponse.Content.ReadFromJsonAsync<LoginResponse>();
            if (loginResponse == null || string.IsNullOrEmpty(loginResponse.SessionId))
            {
                App.AddNotification("Invalid login API response.", true);
                _logger.Warn("Invalid login API response from server: '{Name}'. Response body was null or SessionId was missing.", _server.Info.Name);
                Password = string.Empty;
                return false;
            }

            SaveRememberedCredentials();

            // If login is successful, launch the client
            await LaunchClientAsync(loginResponse.SessionId, loginResponse.LaunchArguments);
            return true;
        }
        catch (Exception ex)
        {
            App.AddNotification($"An exception was thrown while logging in: {ex.Message}.", true);
            _logger.Error(ex, "An exception was thrown while logging into server: {Name}.", _server.Info.Name);
            return false;
        }
    }

    private async Task LaunchClientAsync(string sessionId, string? serverArguments)
    {
        if (!Dx9Helper.IsAvailable())
        {
            await NotifyDirectX9MissingAsync();
            return;
        }

        var launcherArguments = new List<string>
        {
            $"Server={_server.Info.LoginServer}",
            $"SessionId={sessionId}",
            $"Internationalization:Locale={Settings.Instance.Locale}"
        };

        if (!string.IsNullOrEmpty(serverArguments))
            launcherArguments.Add(serverArguments);

        var arguments = string.Join(' ', launcherArguments);
        var workingDirectory = Path.Combine(Constants.SavePath, _server.Info.SavePath, "Client");
        var executablePath = Path.Combine(workingDirectory, Constants.ClientExecutableName);

        if (!File.Exists(executablePath))
        {
            App.AddNotification($"Client executable not found: {executablePath}.", true);
            _logger.Error("Client executable not found for server: '{Name}' at path: {Path}.", _server.Info.Name, executablePath);
            return;
        }

        _server.Process = new Process();
        _server.Process.StartInfo.WorkingDirectory = workingDirectory;

        // Platform-specific process startup logic
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            _server.Process.StartInfo.FileName = "wine";
            _server.Process.StartInfo.Arguments = $"{Constants.ClientExecutableName} {arguments}";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _server.Process.StartInfo.FileName = executablePath;
            _server.Process.StartInfo.Arguments = arguments;
        }
        else
        {
            App.AddNotification("Launching the client is not supported on this OS.", true);
            return;
        }

        _server.Process.StartInfo.UseShellExecute = true;
        _server.Process.EnableRaisingEvents = true;
        _server.Process.Exited += _server.ClientProcessExited;

        try
        {
            _server.Process.Start();
        }
        catch (Exception ex)
        {
            App.AddNotification($"Failed to start the client: {ex.Message}.", true);
            _logger.Error(ex, "Failed to start the client process for server: {Name}.", _server.Info.Name);
        }
    }

    private async Task NotifyDirectX9MissingAsync()
    {
        App.AddNotification("DirectX 9 is not available. Cannot launch the client.", true);

        await Task.Delay(500);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = Constants.DirectXDownloadUrl
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open the DirectX download page automatically.");
            App.AddNotification("Failed to open the DirectX download page. Please open this URL manually: " + Constants.DirectXDownloadUrl, true);
        }
    }
}