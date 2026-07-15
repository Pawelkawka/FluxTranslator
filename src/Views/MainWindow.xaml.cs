using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using FluxTranslator.Core;
using FluxTranslator.Overlay;

namespace FluxTranslator.Views;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private readonly ConfigManager  _cfgManager;
    private readonly AppController  _controller;
    private readonly HotkeyManager  _hotkeys;
    private readonly List<OverlayWindow> _overlays = new();

    private int  _hkTranslateId = -1;
    private int  _hkCopyId      = -1;
    private int  _hkKillId      = -1;
    private bool _exitRequested;
    private string _lastMonitor = "";

    public MainWindow()
    {
        _cfgManager = new ConfigManager();
        _cfgManager.Load();

        _controller = new AppController(_cfgManager);
        _hotkeys    = new HotkeyManager();

        InitializeComponent();

        _controller.StatusChanged    += OnStatusChanged;
        _controller.TranslationReady += _ => { };
        _controller.BackendReady     += OnBackendReady;

        TabGeneral.Initialise   (_cfgManager.Config, _cfgManager, _controller);
        TabAppearance.Initialise(_cfgManager.Config, _cfgManager);
        TabBehavior.Initialise  (_cfgManager.Config, _cfgManager);
        TabHotkeys.Initialise   (_cfgManager.Config, _cfgManager);
        TabTts.Initialise       (_cfgManager.Config, _cfgManager, _controller);

        _lastMonitor = _cfgManager.Config.OverlayMonitor;
        RebuildOverlays();

        TabAppearance.ConfigChanged += () =>
        {
            if (_cfgManager.Config.OverlayMonitor != _lastMonitor)
            {
                _lastMonitor = _cfgManager.Config.OverlayMonitor;
                RebuildOverlays();
            }
            else
            {
                foreach (var ov in _overlays)
                    ov.UpdateConfig(_cfgManager.Config);
            }
        };
        TabHotkeys.HotkeysChanged   += RegisterHotkeys;
        TabTts.TtsSettingsChanged   += () => { };

        TbVersion.Text = AppSettings.AppVersion;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void RebuildOverlays()
    {
        foreach (var old in _overlays)
        {
            try { old.Close(); } catch {}
        }
        _overlays.Clear();

        var screens = System.Windows.Forms.Screen.AllScreens;
        string monitor = _cfgManager.Config.OverlayMonitor;

        if (monitor.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var screen in screens)
            {
                var ov = new OverlayWindow(_cfgManager.Config, screen);
                _overlays.Add(ov);
            }
        }
        else
        {
            System.Windows.Forms.Screen? target = null;
            if (int.TryParse(monitor, out int idx) && idx >= 0 && idx < screens.Length)
            {
                target = screens[idx];
            }
            else
            {
                target = screens.FirstOrDefault();
            }

            if (target is not null)
            {
                var ov = new OverlayWindow(_cfgManager.Config, target);
                _overlays.Add(ov);
            }
        }

    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnableDarkTitleBar();

        _hotkeys.Attach(this);
        RegisterHotkeys();

        RadioButton? activeTab = null;
        foreach (var child in SttSubTabBar.Children)
        {
            if (child is RadioButton { IsChecked: true } rb)
            {
                activeTab = rb;
                break;
            }
        }
        if (activeTab != null)
        {
            UpdateTabIndicator(activeTab, false);
        }
    }

    private async void OnBackendReady()
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            await TabTts.RefreshOutputDevicesAsync();
            await TabTts.RefreshLanguagesAsync();
        });
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hotkeys.Dispose();
        foreach (var ov in _overlays)
        {
            try { ov.Close(); } catch {}
        }
        _overlays.Clear();
        _controller.Dispose();
        AppLogger.Info("Application closed.");
        AppLogger.Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            Hide();
            AppLogger.Info("Window hidden to tray.");
            return;
        }
        base.OnClosing(e);
    }

    public void RequestExit()
    {
        _exitRequested = true;
        Close();
    }

    private void EnableDarkTitleBar()
    {
        try
        {
            var hwnd  = new WindowInteropHelper(this).Handle;
            int value = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not set dark title bar: {ex.Message}");
        }
    }

    private void RegisterHotkeys()
    {
        if (_hkTranslateId >= 0) _hotkeys.Unregister(_hkTranslateId);
        if (_hkCopyId      >= 0) _hotkeys.Unregister(_hkCopyId);
        if (_hkKillId      >= 0) _hotkeys.Unregister(_hkKillId);

        _hkTranslateId = _hotkeys.Register(
            _cfgManager.Config.HotkeyTranslate,
            () => { if (!TabHotkeys.IsAnyHotkeyFocused) _ = _controller.ToggleListeningAsync(); });

        _hkCopyId = _hotkeys.Register(
            _cfgManager.Config.HotkeyCopy,
            () => { if (!TabHotkeys.IsAnyHotkeyFocused) _controller.CopyLastTranslation(); });

        _hkKillId = _hotkeys.Register(
            _cfgManager.Config.HotkeyKillAll,
            () =>
            {
                if (!TabHotkeys.IsAnyHotkeyFocused)
                {
                    _ = _controller.StopAllAsync();
                    foreach (var ov in _overlays) ov.HideOverlay();
                }
            });

        AppLogger.Info($"Hotkeys: {_cfgManager.Config.HotkeyTranslate} / {_cfgManager.Config.HotkeyCopy} / {_cfgManager.Config.HotkeyKillAll}");
    }

    private void OnStatusChanged(StatusEvent ev)
    {
        Dispatcher.Invoke(() =>
        {
            foreach (var ov in _overlays)
                ov.ShowText(ev.Text, ev.IsError, ev.IsFinal, ev.DurationMs);
        });
    }

    private void SubTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;

        if (TabGeneral is null || TabAppearance is null || TabBehavior is null || TabHotkeys is null || TabTts is null) return;

        TabGeneral.Visibility    = Visibility.Collapsed;
        TabAppearance.Visibility = Visibility.Collapsed;
        TabBehavior.Visibility   = Visibility.Collapsed;
        TabHotkeys.Visibility    = Visibility.Collapsed;
        TabTts.Visibility        = Visibility.Collapsed;

        UIElement? target = rb.Tag?.ToString() switch
        {
            "General"    => TabGeneral,
            "Appearance" => TabAppearance,
            "Behavior"   => TabBehavior,
            "Hotkeys"    => TabHotkeys,
            "Tts"        => TabTts,
            _ => null,
        };

        if (target is not null)
        {
            target.Visibility = Visibility.Visible;
            AnimateTab(target);

            if (ReferenceEquals(target, TabTts))
                TabTts.RefreshOutputDevices();
        }

        UpdateTabIndicator(rb, true);
    }

    private static void AnimateTab(UIElement tab)
    {
        tab.RenderTransform = null;

        var fadeIn = new DoubleAnimation
        {
            From     = 0.0,
            To       = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
        };
        tab.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    private void UpdateTabIndicator(RadioButton rb, bool animate)
    {
        if (TabIndicator == null || SttSubTabBar == null) return;

        if (!IsLoaded) return;

        try
        {
            var relativePoint = rb.TransformToAncestor(SttSubTabBar).Transform(new Point(0, 0));
            double targetX = relativePoint.X;
            double targetWidth = rb.ActualWidth;

            if (targetWidth <= 0)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    UpdateTabIndicator(rb, false);
                }));
                return;
            }

            double indicatorWidth = Math.Max(0, targetWidth - 8);
            double indicatorX = targetX + 4;

            if (animate)
            {
                var animX = new DoubleAnimation
                {
                    To = indicatorX,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
                var animW = new DoubleAnimation
                {
                    To = indicatorWidth,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };

                TabIndicator.BeginAnimation(Canvas.LeftProperty, animX);
                TabIndicator.BeginAnimation(WidthProperty, animW);
            }
            else
            {
                TabIndicator.BeginAnimation(Canvas.LeftProperty, null);
                TabIndicator.BeginAnimation(WidthProperty, null);
                Canvas.SetLeft(TabIndicator, indicatorX);
                TabIndicator.Width = indicatorWidth;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tab indicator error: {ex}");
        }
    }
}