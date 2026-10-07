using System.Runtime.InteropServices;
using AIUsage.App.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using static AIUsage.App.Interop.Native;

namespace AIUsage.App.Tray;

/// <summary>
/// Notification area icon via Shell_NotifyIcon. A fixed GUID gives Windows a stable identity so
/// "always show"/pinning survives restarts. Must be created and used on the UI thread.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    // Windows binds this GUID to the executable path; a different path falls back to an ID-based icon.
    private static readonly Guid IconGuid = new("7d3a8f52-4c1e-4b9a-9f0e-2b6c51a4e8d3");
    private const uint CallbackMessage = WM_APP + 1;
    private const string WindowClass = "AIUsage.TrayWindow";

    public enum MenuCommand
    {
        Refresh = 1,
        Settings,
        Autostart,
        Exit,
    }

    private readonly ILogger _logger;
    private readonly WndProc _wndProc; // keeps the delegate alive for native callbacks
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly nint _hwnd;
    private nint _icon;
    private string _tooltip = "";
    private TrayIconState _state = new(TrayIconKind.Pending);
    private bool _useGuid = true;

    public event Action? Clicked;
    public event Action<MenuCommand>? MenuCommandInvoked;

    /// <summary>Supplies menu labels and the autostart check state each time the menu opens.</summary>
    public Func<IReadOnlyList<(MenuCommand Command, string Text, bool Checked)>> MenuItems { get; set; } = () => [];

    public TrayIcon(ILogger<TrayIcon> logger)
    {
        _logger = logger;
        _wndProc = WindowProc;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = WindowClass,
        };
        RegisterClassEx(ref wc);
        // A hidden top-level window (not message-only) so it receives the TaskbarCreated broadcast.
        _hwnd = CreateWindowEx(0, WindowClass, "", 0, 0, 0, 0, 0, 0, 0, wc.hInstance, 0);

        SetPreferredAppMode(1); // AllowDark: native menu follows the system theme
        Add();
    }

    public static bool IsLightTaskbar() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;

    public void Update(TrayIconState state, string tooltip)
    {
        _state = state;
        _tooltip = tooltip.Length > 127 ? tooltip[..126] + "…" : tooltip;
        RenderIcon();
        var data = CreateData(NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>Icon bounds in physical screen pixels, or null if hidden in the overflow area.</summary>
    public RECT? GetIconRect()
    {
        var id = new NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>() };
        if (_useGuid)
        {
            id.guidItem = IconGuid;
        }
        else
        {
            id.hWnd = _hwnd;
            id.uID = 1;
        }

        return Shell_NotifyIconGetRect(ref id, out var rect) == 0 && rect.Width > 0 ? rect : null;
    }

    public void Dispose()
    {
        var data = CreateData(0);
        Shell_NotifyIcon(NIM_DELETE, ref data);
        DestroyWindow(_hwnd);
        if (_icon != 0)
        {
            DestroyIcon(_icon);
        }
    }

    private void Add()
    {
        RenderIcon();
        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        if (!Shell_NotifyIcon(NIM_ADD, ref data) && _useGuid)
        {
            // Stale registration (e.g. after a crash) blocks NIM_ADD; remove it and retry once.
            Shell_NotifyIcon(NIM_DELETE, ref data);
            if (!Shell_NotifyIcon(NIM_ADD, ref data))
            {
                _logger.LogWarning("Tray icon GUID is registered to another executable path; falling back to an ID-based icon");
                _useGuid = false;
                data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
                Shell_NotifyIcon(NIM_ADD, ref data);
            }
        }

        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    private NOTIFYICONDATA CreateData(uint flags)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = _tooltip,
            szInfo = "",
            szInfoTitle = "",
        };
        if (_useGuid)
        {
            data.uFlags |= NIF_GUID;
            data.guidItem = IconGuid;
        }
        else
        {
            data.uID = 1;
        }

        return data;
    }

    private void RenderIcon()
    {
        var old = _icon;
        _icon = TrayIconRenderer.Render(_state, IconSize(), IsLightTaskbar());
        if (old != 0)
        {
            DestroyIcon(old);
        }
    }

    // Small icon size at the DPI of the monitor hosting the icon (16/20/24/32 px at 100–200 %).
    private int IconSize()
    {
        var rect = GetIconRect() ?? default;
        var monitor = MonitorFromRect(rect, MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpi, out _) == 0
            ? GetSystemMetricsForDpi(SM_CXSMICON, dpi)
            : 16;
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == CallbackMessage)
        {
            switch ((int)(lParam & 0xFFFF))
            {
                case NIN_SELECT or NIN_KEYSELECT:
                    Clicked?.Invoke();
                    break;
                case WM_CONTEXTMENU:
                    ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                    break;
            }

            return 0;
        }

        if (msg == _taskbarCreated)
        {
            Add(); // Explorer restarted
        }
        else if (msg is WM_SETTINGCHANGE or WM_DISPLAYCHANGE or WM_DPICHANGED)
        {
            // Theme switch (ImmersiveColorSet) or DPI change: re-render with the new contrast/size.
            FlushMenuThemes();
            Update(_state, _tooltip);
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu(int x, int y)
    {
        var menu = CreatePopupMenu();
        try
        {
            foreach (var (command, text, isChecked) in MenuItems())
            {
                if (command == MenuCommand.Exit)
                {
                    AppendMenu(menu, MF_SEPARATOR, 0, null);
                }

                AppendMenu(menu, MF_STRING | (isChecked ? MF_CHECKED : 0), (nuint)command, text);
            }

            // Required so the menu closes when clicking elsewhere.
            SetForegroundWindow(_hwnd);
            var selected = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_RIGHTALIGN, x, y, _hwnd, 0);
            PostMessage(_hwnd, WM_NULL, 0, 0);
            if (selected != 0)
            {
                MenuCommandInvoked?.Invoke((MenuCommand)selected);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }
}
