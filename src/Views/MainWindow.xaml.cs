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
    private readonly OverlayWindow  _overlay;

    private int  _hkTranslateId = -1;
    private int  _hkCopyId      = -1;
    private int  _hkKillId      = -1;
    private bool _exitRequested;

    public MainWindow()
    {
        _cfgManager = new ConfigManager();
        _cfgManager.Load();

        _controller = new AppController(_cfgManager);
        _hotkeys    = new HotkeyManager();

        InitializeComponent();

        _overlay = new OverlayWindow(_cfgManager.Config);

        _controller.StatusChanged    += OnStatusChanged;
        _controller.TranslationReady += _ => { };
        _controller.BackendReady     += OnBackendReady;

        TabGeneral.Initialise   (_cfgManager.Config, _cfgManager, _controller);
        TabAppearance.Initialise(_cfgManager.Config, _cfgManager);
        TabBehavior.Initialise  (_cfgManager.Config, _cfgManager);
        TabHotkeys.Initialise   (_cfgManager.Config, _cfgManager);
        TabTts.Initialise       (_cfgManager.Config, _cfgManager, _controller);

        TabAppearance.ConfigChanged += () => _overlay.UpdateConfig(_cfgManager.Config);
        TabHotkeys.HotkeysChanged   += RegisterHotkeys;
        TabTts.TtsSettingsChanged   += () => { };

        TbVersion.Text = AppSettings.AppVersion;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnableDarkTitleBar();

        _hotkeys.Attach(this);
        RegisterHotkeys();
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
        _overlay.Close();
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
            () => { if (!TabHotkeys.IsAnyHotkeyFocused) { _ = _controller.StopAllAsync(); _overlay.HideOverlay(); } });

        AppLogger.Info($"Hotkeys: {_cfgManager.Config.HotkeyTranslate} / {_cfgManager.Config.HotkeyCopy} / {_cfgManager.Config.HotkeyKillAll}");
    }

    private void OnStatusChanged(StatusEvent ev)
    {
        Dispatcher.Invoke(() =>
            _overlay.ShowText(ev.Text, ev.IsError, ev.IsFinal, ev.DurationMs));
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
}