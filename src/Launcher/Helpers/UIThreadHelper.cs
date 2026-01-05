using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace Launcher.Helpers;

public static class UIThreadHelper
{
    public static async Task InvokeAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await action();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action);
        }
    }
}