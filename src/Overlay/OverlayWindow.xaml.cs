using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FluxTranslator.Core;

namespace FluxTranslator.Overlay;

public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern int  GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int  SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED     = 0x00080000;

    private AppConfig      _config;
    private readonly DispatcherTimer _hideTimer;

    public OverlayWindow(AppConfig config)
    {
        InitializeComponent();
        _config    = config;
        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) => HideOverlay();

        Loaded += OnLoaded;
        ApplyStyle();
        Hide();
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

        Dispatcher.BeginInvoke(() =>
        {
            DisplayText.Text = text;
            DisplayText.Foreground = new SolidColorBrush(
                isError
                ? Color.FromRgb(0xF4, 0x43, 0x36)
                : ParseColor(_config.TextColor, Colors.White));

            _hideTimer.Stop();

            if (!IsVisible)
            {
                Left = -32000;
                Top  = -32000;
                Show();
            }
            Dispatcher.BeginInvoke(PositionWindow,
                System.Windows.Threading.DispatcherPriority.Render);

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
        Dispatcher.BeginInvoke(() =>
        {
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

    private void PositionWindow()
    {
        UpdateLayout();
        var screen  = SystemParameters.WorkArea;
        double w    = ActualWidth  > 0 ? ActualWidth  : MinWidth;
        double h    = ActualHeight > 0 ? ActualHeight : MinHeight;
        const int margin = 20;

        (Left, Top) = _config.OverlayPosition switch
        {
            "top_left"      => (screen.Left  + margin,                   screen.Top  + margin),
            "top_center"    => (screen.Left  + (screen.Width  - w) / 2,  screen.Top  + margin),
            "top_right"     => (screen.Right - w - margin,               screen.Top  + margin),
            "bottom_left"   => (screen.Left  + margin,                   screen.Bottom - h - margin),
            "bottom_center" => (screen.Left  + (screen.Width  - w) / 2,  screen.Bottom - h - margin),
            "bottom_right"  => (screen.Right - w - margin,               screen.Bottom - h - margin),
            _               => (screen.Left  + (screen.Width  - w) / 2,  screen.Top  + margin),
        };
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }
}
