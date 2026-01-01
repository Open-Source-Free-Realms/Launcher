using System;
using Avalonia.Controls;
using Launcher.ViewModels;

namespace Launcher.Views;

public partial class GraphicsSettings : Window
{
    public GraphicsSettings()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.GraphicsSettings vm)
        {
            vm.CloseAction = Close;
        }
    }
}