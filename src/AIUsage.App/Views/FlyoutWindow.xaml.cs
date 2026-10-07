using System.Diagnostics;
using AIUsage.App.ViewModels;
using AIUsage.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using WinRT.Interop;
using static AIUsage.App.Interop.Native;

namespace AIUsage.App.Views;

/// <summary>Borderless, topmost flyout anchored to the tray icon with a slide + fade animation.</summary>
public sealed partial class FlyoutWindow : Window
{
    private const double WidthDip = 360;
    private const double MarginDip = 12;
    private const double SlideDip = 40;
    private static readonly TimeSpan ShowDuration = TimeSpan.FromMilliseconds(230);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(140);
    // Clicking the tray icon while open first deactivates (hides) the flyout; the click itself must not reopen it.
    private static readonly TimeSpan ReopenDebounce = TimeSpan.FromMilliseconds(400);

    private enum Edge { Bottom, Top, Left, Right }

    private readonly nint _hwnd;
    private readonly Func<RECT?> _getAnchor;
    private readonly UISettings _uiSettings = new();
    private readonly DispatcherQueueTimer _countdownTimer;
    private readonly Stopwatch _sinceHidden = Stopwatch.StartNew();
    private Action? _stopAnimation;

    public FlyoutViewModel ViewModel { get; }
    public bool IsOpen { get; private set; }

    internal FlyoutWindow(FlyoutViewModel viewModel, Func<RECT?> getAnchor)
    {
        ViewModel = viewModel;
        _getAnchor = getAnchor;
        InitializeComponent();
        Title = "AI Usage";
        _hwnd = WindowNative.GetWindowHandle(this);

        // Border without title bar keeps the Windows 11 rounded corners and shadow.
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true; // Alt+F4 only hides
            Hide();
        };

        // Tool window: no taskbar button. Layered: window-wide alpha for the fade.
        var exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (nint)(exStyle | (nint)WS_EX_TOOLWINDOW | (nint)WS_EX_LAYERED));
        SetAlpha(255);

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && IsOpen)
            {
                Hide();
            }
        };

        _countdownTimer = DispatcherQueue.CreateTimer();
        _countdownTimer.Interval = TimeSpan.FromSeconds(20);
        _countdownTimer.Tick += (_, _) => ViewModel.UpdateCountdowns();

        ViewModel.RefreshCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.RefreshCommand.IsRunning))
            {
                if (ViewModel.RefreshCommand.IsRunning)
                {
                    RefreshSpin.Begin();
                }
                else
                {
                    RefreshSpin.Stop();
                }
            }
        };
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Hide();
        }
        else if (_sinceHidden.Elapsed > ReopenDebounce)
        {
            Show();
        }
    }

    public void Show()
    {
        if (IsOpen)
        {
            SetForegroundWindow(_hwnd);
            return;
        }

        IsOpen = true;
        _stopAnimation?.Invoke();
        var animate = _uiSettings.AnimationsEnabled;

        // Show invisibly first so the content is in the live tree and can be measured.
        SetAlpha(0);
        AppWindow.Show(activateWindow: true);
        Activate();
        SetForegroundWindow(_hwnd);

        var (bounds, edge) = ComputePlacement();
        var slide = SlideOffset(edge, bounds.Scale);
        var from = animate ? new PointInt32(bounds.Rect.X + slide.X, bounds.Rect.Y + slide.Y) : new PointInt32(bounds.Rect.X, bounds.Rect.Y);
        // Twice: the first move may cross monitors and trigger a DPI-driven resize.
        AppWindow.MoveAndResize(new RectInt32(from.X, from.Y, bounds.Rect.Width, bounds.Rect.Height));
        AppWindow.MoveAndResize(new RectInt32(from.X, from.Y, bounds.Rect.Width, bounds.Rect.Height));

        // Pointer focus state: Esc works without drawing a keyboard focus rectangle.
        RefreshButton.Focus(FocusState.Pointer);
        ViewModel.UpdateCountdowns();
        ViewModel.ExpandBars();
        _countdownTimer.Start();

        if (animate)
        {
            Animate(from, new PointInt32(bounds.Rect.X, bounds.Rect.Y), 0, 255, ShowDuration, easeOut: true, onCompleted: null);
        }
        else
        {
            SetAlpha(255);
        }
    }

    public void Hide()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        _sinceHidden.Restart();
        _countdownTimer.Stop();
        _stopAnimation?.Invoke();

        void Finish()
        {
            AppWindow.Hide();
            ViewModel.CollapseBars();
        }

        if (!_uiSettings.AnimationsEnabled)
        {
            Finish();
            return;
        }

        var (_, edge) = ComputePlacement();
        var slide = SlideOffset(edge, XamlRootScale());
        var position = AppWindow.Position;
        Animate(position, new PointInt32(position.X + slide.X / 2, position.Y + slide.Y / 2), 255, 0, HideDuration, easeOut: false, Finish);
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Hide();
    }

    private void Animate(PointInt32 from, PointInt32 to, byte fromAlpha, byte toAlpha, TimeSpan duration, bool easeOut, Action? onCompleted)
    {
        var clock = Stopwatch.StartNew();
        EventHandler<object>? onFrame = null;
        onFrame = (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed / duration);
            var eased = easeOut ? 1 - Math.Pow(1 - t, 3) : t * t * t;
            AppWindow.Move(new PointInt32(Lerp(from.X, to.X, eased), Lerp(from.Y, to.Y, eased)));
            SetAlpha((byte)Lerp(fromAlpha, toAlpha, eased));
            if (t >= 1)
            {
                _stopAnimation!();
                onCompleted?.Invoke();
            }
        };
        _stopAnimation = () =>
        {
            CompositionTarget.Rendering -= onFrame;
            _stopAnimation = null;
        };
        CompositionTarget.Rendering += onFrame;
    }

    private static int Lerp(int from, int to, double t) => (int)Math.Round(from + (to - from) * t);

    private void SetAlpha(byte alpha) => SetLayeredWindowAttributes(_hwnd, 0, alpha, LWA_ALPHA);

    private double XamlRootScale() => Content.XamlRoot?.RasterizationScale ?? 1;

    private ((RectInt32 Rect, double Scale), Edge) ComputePlacement()
    {
        RECT anchor;
        if (_getAnchor() is { } iconRect)
        {
            anchor = iconRect;
        }
        else
        {
            // Icon hidden in the overflow area: anchor at the cursor instead.
            GetCursorPos(out var cursor);
            anchor = new RECT { Left = cursor.X, Top = cursor.Y, Right = cursor.X + 1, Bottom = cursor.Y + 1 };
        }

        var monitor = MonitorFromRect(anchor, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1;
        var work = info.rcWork;
        var edge = NearestEdge(anchor, info.rcMonitor);

        Content.Measure(new Size(WidthDip, double.PositiveInfinity));
        var heightDip = Content.DesiredSize.Height > 0 ? Content.DesiredSize.Height : 320;
        var margin = (int)Math.Round(MarginDip * scale);
        // The window size includes the thin border, the measured content does not; without this the footer gets clipped.
        var frameWidth = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        var frameHeight = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        var width = (int)Math.Round(WidthDip * scale) + frameWidth;
        var height = Math.Min((int)Math.Round(heightDip * scale) + frameHeight, work.Height - 2 * margin);

        var centerX = (anchor.Left + anchor.Right) / 2;
        var centerY = (anchor.Top + anchor.Bottom) / 2;
        var (x, y) = edge switch
        {
            Edge.Top => (centerX - width / 2, work.Top + margin),
            Edge.Left => (work.Left + margin, centerY - height / 2),
            Edge.Right => (work.Right - margin - width, centerY - height / 2),
            _ => (centerX - width / 2, work.Bottom - margin - height),
        };

        // Keep the whole flyout inside the work area.
        x = Math.Clamp(x, work.Left + margin, work.Right - margin - width);
        y = Math.Clamp(y, work.Top + margin, work.Bottom - margin - height);
        return ((new RectInt32(x, y, width, height), scale), edge);
    }

    private static Edge NearestEdge(RECT anchor, RECT monitor)
    {
        var cx = (anchor.Left + anchor.Right) / 2;
        var cy = (anchor.Top + anchor.Bottom) / 2;
        var distances = new[]
        {
            (Edge.Bottom, monitor.Bottom - cy),
            (Edge.Top, cy - monitor.Top),
            (Edge.Left, cx - monitor.Left),
            (Edge.Right, monitor.Right - cx),
        };
        return distances.MinBy(d => d.Item2).Item1;
    }

    // Slide in from the taskbar edge: the start position is offset towards that edge.
    private static PointInt32 SlideOffset(Edge edge, double scale)
    {
        var d = (int)Math.Round(SlideDip * scale);
        return edge switch
        {
            Edge.Top => new PointInt32(0, -d),
            Edge.Left => new PointInt32(-d, 0),
            Edge.Right => new PointInt32(d, 0),
            _ => new PointInt32(0, d),
        };
    }

    public static Visibility VisibleIf(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public static bool HasText(string? text) => !string.IsNullOrEmpty(text);

    public static Brush LevelBrush(UsageLevel level) => (Brush)Application.Current.Resources[$"Level{level}Brush"];
}
