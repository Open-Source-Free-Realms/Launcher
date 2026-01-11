using System;
using System.Diagnostics;

using Avalonia.Controls;

namespace Launcher.Views;

public partial class Server : UserControl
{
    public Server()
    {
        InitializeComponent();
    }

    protected override async void OnDataContextBeginUpdate()
    {
        try
        {
            if (DataContext is not ViewModels.Server server)
                return;

            var success = await server.OnShowAsync();

            if (!success)
                App.ClearServerSelection();
        }
        catch (Exception ex)
        {
            // Catch this, because it's a fire and forget method, to prevent crashes.
            Debug.WriteLine(ex);
        }
    }
}