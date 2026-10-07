using AIUsage.App.ViewModels;
using AIUsage.Core.Resources;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using static AIUsage.App.Interop.Native;

namespace AIUsage.App.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = Strings.Settings_Title;
        // The caption bar defaults to the legacy light theme and the generic icon.
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AIUsage.ico"));
        CenterOnCursorMonitor(480, 760);
        viewModel.Closed += Close;
    }

    // The window has no DPI of its own before it is shown, so size it for the monitor it will appear on.
    private void CenterOnCursorMonitor(int widthDip, int heightDip)
    {
        GetCursorPos(out var cursor);
        var monitor = MonitorFromRect(new RECT { Left = cursor.X, Top = cursor.Y, Right = cursor.X + 1, Bottom = cursor.Y + 1 }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1;
        var work = info.rcWork;
        var width = Math.Min((int)(widthDip * scale), work.Width);
        var height = Math.Min((int)(heightDip * scale), work.Height);
        AppWindow.MoveAndResize(new RectInt32(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height));
    }
}
