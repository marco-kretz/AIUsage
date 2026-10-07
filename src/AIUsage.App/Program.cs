using AIUsage.App.Interop;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AIUsage.App;

public static class Program
{
    private const string MutexName = @"Local\AIUsage.SingleInstance";
    private const string ShowEventName = @"Local\AIUsage.ShowFlyout";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var isFirstInstance);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!isFirstInstance)
        {
            // Let the running instance take the foreground, then ask it to show the flyout.
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            showEvent.Set();
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(showEvent);
        });
    }
}
