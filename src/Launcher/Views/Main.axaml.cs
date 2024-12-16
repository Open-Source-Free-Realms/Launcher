using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Launcher.Views;

public partial class Main : Window
{
    public readonly ViewModels.Main ViewModel = new ViewModels.Main();

    public Main()
    {
        DataContext = ViewModel;

        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        ViewModel.OnLoad();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            App.CancelPopup();
            e.Handled = true;

            return;
        }

        base.OnKeyDown(e);
    }

    private void OnPopupSure(object sender, RoutedEventArgs e)
    {
        App.ProcessPopup();

        e.Handled = true;
    }

    private void OnPopupCancel(object sender, RoutedEventArgs e)
    {
        App.CancelPopup();

        e.Handled = true;
    }

    private void OnPopupCancelByClickMask(object sender, PointerPressedEventArgs e)
    {
        OnPopupCancel(sender, e);
    }
}