using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Helpers;
using Launcher.Models;
using Launcher.Services;
using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Launcher.ViewModels;

public partial class Login : Popup
{
    private readonly Server _server;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty] private string? warning;
    [Required] [ObservableProperty] [NotifyDataErrorInfo] private string username = string.Empty;
    [Required] [ObservableProperty] [NotifyDataErrorInfo] private string password = string.Empty;
    [ObservableProperty] private bool rememberUsername;
    [ObservableProperty] private bool rememberPassword;

    public bool AutoFocusUsername => string.IsNullOrEmpty(Username);
    public bool AutoFocusPassword => !string.IsNullOrEmpty(Username) && string.IsNullOrEmpty(Password);
    public IAsyncRelayCommand LoginCommand { get; }
    public ICommand LoginCancelCommand { get; }

    public Login(Server server)
    {
        _server = server;
        AddSecureWarning();
        RememberUsername = _server.Info.RememberUsername;
        RememberPassword = _server.Info.RememberPassword;
        Username = RememberUsername ? _server.Info.Username ?? string.Empty : string.Empty;
        Password = RememberPassword ? _server.Info.Password ?? string.Empty : string.Empty;
        LoginCommand = new AsyncRelayCommand(OnLogin);
        LoginCancelCommand = new RelayCommand(OnLoginCancel);
        View = new Views.Login { DataContext = this };
    }

    private Task OnLogin() => App.ProcessPopupAsync();
    private void OnLoginCancel() => App.CancelPopup();

    private void AddSecureWarning()
    {
        if (Uri.TryCreate(_server.Info.LoginApiUrl, UriKind.Absolute, out var loginApiUrl) && loginApiUrl.Scheme != Uri.UriSchemeHttps)
            Warning = App.GetText("Text.Login.SecureApiWarning");
    }

    public override async Task<bool> ProcessAsync()
    {
        // FIX: We must update the boolean flags in the ServerInfo BEFORE saving!
        _server.Info.RememberUsername = RememberUsername;
        _server.Info.RememberPassword = RememberPassword;

        _server.Info.Username = RememberUsername ? Username : null;
        _server.Info.Password = RememberPassword ? Password : null;
        
        Settings.Instance.Save();
        
        try
        {
            using var client = HttpHelper.CreateHttpClient();
            var resp = await client.PostAsJsonAsync(_server.Info.LoginApiUrl, new LoginRequest { Username = Username, Password = Password });
            
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                await App.AddNotification(App.GetText("Text.Login.Unauthorized"), true);
                Password = string.Empty;
                return false;
            }
            if (!resp.IsSuccessStatusCode)
            {
                await App.AddNotification($"Login Failed: {resp.ReasonPhrase}", true);
                return false;
            }

            var data = await resp.Content.ReadFromJsonAsync<LoginResponse>();
            if (data == null || string.IsNullOrEmpty(data.SessionId))
            {
                await App.AddNotification("Invalid API response.", true);
                return false;
            }

            await LaunchClientAsync(data.SessionId, data.LaunchArguments);
            return true;
        }
        catch (Exception ex) { await App.AddNotification($"Error: {ex.Message}", true); return false; }
    }

    private async Task LaunchClientAsync(string sessionId, string? serverArguments)
    {
        var workingDir = Path.Combine(Constants.SavePath, _server.Info.SavePath, "Client");
        var exePath = Path.Combine(workingDir, Constants.ClientExecutableName);
        if (!File.Exists(exePath)) { await App.AddNotification($"Client missing: {exePath}", true); return; }

        var args = new List<string> { $"Server={_server.Info.LoginServer}", $"SessionId={sessionId}", $"Internationalization:Locale={Settings.Instance.Locale}" };
        if (!string.IsNullOrEmpty(serverArguments)) args.Add(serverArguments);
        var argsStr = string.Join(' ', args);

        _server.Process = new Process();
        _server.Process.StartInfo.WorkingDirectory = workingDir;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string bin = WineSetupService.EngineBin;
            if (string.IsNullOrEmpty(bin)) { await App.AddNotification("Engine missing.", true); return; }
            _server.Process.StartInfo.FileName = bin;
            _server.Process.StartInfo.Arguments = $"\"{Constants.ClientExecutableName}\" {argsStr}";
            _server.Process.StartInfo.EnvironmentVariables["WINEPREFIX"] = WineSetupService.PrefixPath;
            _server.Process.StartInfo.EnvironmentVariables["WINE_NOCRASHDIALOG"] = "1";
            
            string wineBase = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(bin))) ?? string.Empty;
            if (Directory.Exists(Path.Combine(wineBase, "lib")))
                _server.Process.StartInfo.EnvironmentVariables["DYLD_LIBRARY_PATH"] = Path.Combine(wineBase, "lib");
            _server.Process.StartInfo.UseShellExecute = false;
        }
        else
        {
            _server.Process.StartInfo.FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "wine" : exePath;
            _server.Process.StartInfo.Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? $"{Constants.ClientExecutableName} {argsStr}" : argsStr;
            _server.Process.StartInfo.UseShellExecute = true;
        }
        
        _server.Process.EnableRaisingEvents = true;
        _server.Process.Exited += _server.ClientProcessExited;
        _server.Process.Start();
    }
}