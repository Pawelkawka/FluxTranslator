using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FluxTranslator.Core;

namespace FluxTranslator.Overlay;

public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern int  GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int  SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED     = 0x00080000;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    private AppConfig      _config;
    private readonly DispatcherTimer _hideTimer;
    private Screen?        _targetScreen;

    public OverlayWindow(AppConfig config, Screen? targetScreen = null)
    {
        InitializeComponent();
        _config      = config;
        _targetScreen  = targetScreen;
        _hideTimer     = new DispatcherTimer();
        _hideTimer.Tick += (_, _) => HideOverlay();

        Loaded += OnLoaded;
        ApplyStyle();
        MinWidth  = 250;
        MinHeight = 40;
        Hide();
    }

    public void SetTargetScreen(Screen? screen)
    {
        _targetScreen = screen;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MakeClickThrough();
    }

    public void UpdateConfig(AppConfig config)
    {
        _config = config;
        Dispatcher.Invoke(ApplyStyle);
    }

    public void ShowText(string text, bool isError, bool isFinal, int durationMs = 0)
    {
        if (string.IsNullOrEmpty(text)) { HideOverlay(); return; }
        if (Dispatcher.HasShutdownStarted) return;

        Dispatcher.BeginInvoke(() =>
        {
            if (Dispatcher.HasShutdownStarted) return;

            DisplayText.Text = text;
            DisplayText.Foreground = new SolidColorBrush(
                isError
                ? Color.FromRgb(0xF4, 0x43, 0x36)
                : ParseColor(_config.TextColor, Colors.White));

            _hideTimer.Stop();

            if (!IsVisible)
            {
                PositionWindow(MinWidth, MinHeight);
                Show();
            }
            UpdateLayout();
            PositionWindow();

            if (isFinal)
            {
                int ms = durationMs > 0 ? durationMs : _config.OverlayDisplayTime * 1000;
                _hideTimer.Interval = TimeSpan.FromMilliseconds(ms);
                _hideTimer.Start();
            }
        });
    }

    public void HideOverlay()
    {
        if (Dispatcher.HasShutdownStarted) return;

        Dispatcher.BeginInvoke(() =>
        {
            if (Dispatcher.HasShutdownStarted) return;

            _hideTimer.Stop();
            Hide();
            DisplayText.Text = string.Empty;
        });
    }

    private void ApplyStyle()
    {
        // font
        DisplayText.FontFamily = new FontFamily(_config.FontFamily);
        DisplayText.FontSize   = _config.FontSize;
        DisplayText.FontWeight = _config.FontBold ? FontWeights.Bold : FontWeights.Normal;
        DisplayText.MaxWidth   = 800;
        DisplayText.MinWidth   = 250;

        // background
        var bg   = ParseColor(_config.BackgroundColor, Colors.Black);
        var alpha = (byte)Math.Round(_config.BackgroundOpacity / 100.0 * 255);
        BgBrush.Color = Color.FromArgb(alpha, bg.R, bg.G, bg.B);

        // border + corner + padding
        RootBorder.Padding      = new Thickness(_config.Padding);
        RootBorder.CornerRadius = new CornerRadius(_config.CornerRadius);

        if (_config.BorderWidth > 0)
        {
            RootBorder.BorderThickness = new Thickness(_config.BorderWidth);
            RootBorder.BorderBrush     = new SolidColorBrush(
                ParseColor(_config.BorderColor, Colors.Gray));
        }
        else
        {
            RootBorder.BorderThickness = new Thickness(0);
        }
    }

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }

    public void PositionWindow(double? width = null, double? height = null, Screen? targetScreen = null)
    {
        var screen = targetScreen ?? _targetScreen ?? Screen.PrimaryScreen;
        var hwnd = new WindowInteropHelper(this).Handle;

        double left, top, right, bottom, areaWidth, areaHeight;

        if (hwnd != IntPtr.Zero && screen is not null)
        {
            var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            double scaleX = 1.0, scaleY = 1.0;
            if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0)
            {
                scaleX = 96.0 / dpiX;
                scaleY = 96.0 / dpiY;
            }

            var area = screen.WorkingArea;
            left   = area.Left   * scaleX;
            top    = area.Top    * scaleY;
            right  = area.Right  * scaleX;
            bottom = area.Bottom * scaleY;
            areaWidth  = area.Width  * scaleX;
            areaHeight = area.Height * scaleY;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            left   = wa.Left;
            top    = wa.Top;
            right  = wa.Right;
            bottom = wa.Bottom;
            areaWidth  = wa.Width;
            areaHeight = wa.Height;
        }

        double w = width ?? (ActualWidth  > 0 ? ActualWidth  : MinWidth);
        double h = height ?? (ActualHeight > 0 ? ActualHeight : MinHeight);
        const int margin = 20;

        (Left, Top) = _config.OverlayPosition switch
        {
            "top_left"      => (left  + margin,                  top  + margin),
            "top_center"    => (left  + (areaWidth  - w) / 2,    top  + margin),
            "top_right"     => (right - w - margin,              top  + margin),
            "bottom_left"   => (left  + margin,                  bottom - h - margin),
            "bottom_center" => (left  + (areaWidth  - w) / 2,    bottom - h - margin),
            "bottom_right"  => (right - w - margin,              bottom - h - margin),
            _               => (left  + (areaWidth  - w) / 2,    top  + margin),
        };
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }
}
