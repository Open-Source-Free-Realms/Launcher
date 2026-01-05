using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Services;
using System;
using System.Threading.Tasks;

namespace Launcher.ViewModels;

public partial class WineSetup : Popup
{
    [ObservableProperty] private double progressValue;
    [ObservableProperty] private string statusText = "The Wine Game Engine is missing. Install now? (~500MB)";
    [ObservableProperty] private bool isInstalling = false;
    [ObservableProperty] private string titleText = "Wine Game Engine Setup";

    private TaskCompletionSource<bool> _userDecision = new();

    public WineSetup()
    {
        View = new Views.WineSetupView { DataContext = this };
    }

    [RelayCommand]
    public void StartInstall()
    {
        IsInstalling = true;
        _userDecision.TrySetResult(true);
    }

    [RelayCommand]
    public void Cancel() => _userDecision.TrySetResult(false);

    public override async Task<bool> ProcessAsync()
    {
        bool userAgreed = await _userDecision.Task;
        if (!userAgreed) return false;

        IsInstalling = true;
        var progress = new Progress<double>(p => ProgressValue = p);
        var status = new Progress<string>(s => StatusText = s);

        try
        {
            await WineSetupService.Install(status, progress);
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            await Task.Delay(4000);
            return false;
        }
    }
}