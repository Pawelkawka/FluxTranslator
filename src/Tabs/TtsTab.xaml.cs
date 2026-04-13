using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FluxTranslator.Core;

namespace FluxTranslator.Tabs;

public partial class TtsTab : UserControl
{
    private AppConfig?      _config;
    private ConfigManager?  _manager;
    private AppController?  _controller;
    private bool            _loading;

    public event Action? TtsSettingsChanged;

    public TtsTab() => InitializeComponent();

    public void Initialise(AppConfig config, ConfigManager manager, AppController? controller = null)
    {
        _config     = config;
        _manager    = manager;
        _controller = controller;
        LoadValues();
    }

    public void Refresh()
    {
        if (_config is not null)
            LoadValues();
    }

    private async void LoadValues()
    {
        _loading = true;

        // load tts enabled state
        ChkTtsEnabled.IsChecked = _config!.TtsEnabled;
        UpdateTtsStatus();

        // load rate/volume/pitch options
        LoadRateOptions();
        LoadVolumeOptions();
        LoadPitchOptions();

        // load output devices
        await LoadOutputDevicesAsync();

        // load languages and voices
        await LoadLanguagesAsync();

        // set current values
        CbRate.SelectedValue = _config.TtsRate;
        CbVolume.SelectedValue = _config.TtsVolume;
        CbPitch.SelectedValue = _config.TtsPitch;

        _loading = false;
    }

    private void LoadRateOptions()
    {
        CbRate.Items.Clear();
        var rates = new[] { "-50%", "-30%", "-20%", "-10%", "+0%", "+10%", "+20%", "+30%", "+50%" };
        foreach (var rate in rates)
        {
            CbRate.Items.Add(new ComboBoxItem { Content = rate, Tag = rate });
        }
    }

    private void LoadVolumeOptions()
    {
        CbVolume.Items.Clear();
        var volumes = new[] { "-50%", "-30%", "-20%", "-10%", "+0%", "+10%", "+20%", "+30%", "+50%" };
        foreach (var volume in volumes)
        {
            CbVolume.Items.Add(new ComboBoxItem { Content = volume, Tag = volume });
        }
    }

    private void LoadPitchOptions()
    {
        CbPitch.Items.Clear();
        var pitches = new[] { "-20Hz", "-10Hz", "+0Hz", "+10Hz", "+20Hz" };
        foreach (var pitch in pitches)
        {
            CbPitch.Items.Add(new ComboBoxItem { Content = pitch, Tag = pitch });
        }
    }

    private async Task LoadOutputDevicesAsync()
    {
        CbOutputDevice.Items.Clear();

        CbOutputDevice.Items.Add(new ComboBoxItem
        {
            Content = "System Default",
            Tag = -1
        });

        try
        {
            var client = new SttBridgeClient(AppSettings.SttPort);
            var devices = await client.ListDevicesAsync().ConfigureAwait(false);

            // Switch back to UI thread for updating controls
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var device in devices)
                {
                    var label = device.IsDefault ? $"{device.Name} (Default)" : device.Name;
                    CbOutputDevice.Items.Add(new ComboBoxItem
                    {
                        Content = label,
                        Tag = device.Id
                    });
                }

                // current device
                int targetId = _config!.TtsOutputDeviceId;
                int idx = 0;
                foreach (ComboBoxItem item in CbOutputDevice.Items)
                {
                    if (item.Tag is int id && id == targetId)
                    {
                        CbOutputDevice.SelectedIndex = idx;
                        break;
                    }
                    idx++;
                }
            });

            client.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not load TTS devices: {ex.Message}");
        }
    }

    private async Task LoadLanguagesAsync()
    {
        CbLanguage.Items.Clear();

        try
        {
            var client = new SttBridgeClient(AppSettings.SttPort);
            var languages = await client.ListLanguagesAsync().ConfigureAwait(false);

            // Switch back to UI thread for updating controls
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var language in languages.OrderBy(l => l.Name))
                {
                    CbLanguage.Items.Add(new ComboBoxItem
                    {
                        Content = language.Name,
                        Tag = language.Code
                    });
                }

                // select current language
                string targetLang = _config!.TtsLanguage;
                int idx = 0;
                foreach (ComboBoxItem item in CbLanguage.Items)
                {
                    if (item.Tag is string tag && tag == targetLang)
                    {
                        CbLanguage.SelectedIndex = idx;
                        break;
                    }
                    idx++;
                }
            });

            client.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not load TTS languages: {ex.Message}");
        }

        await LoadVoicesForLanguageAsync(_config!.TtsLanguage);
    }

    private async Task LoadVoicesForLanguageAsync(string langCode)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CbVoice.Items.Clear();
        });

        if (string.IsNullOrEmpty(langCode)) return;

        try
        {
            var client = new SttBridgeClient(AppSettings.SttPort);
            var languages = await client.ListLanguagesAsync().ConfigureAwait(false);

            var lang = languages.FirstOrDefault(l => l.Code == langCode);
            if (lang == null)
            {
                AppLogger.Warn($"Language '{langCode}' not found in TTS languages list");
                return;
            }

            var voices = lang.Voices;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var voice in voices)
                {
                    CbVoice.Items.Add(new ComboBoxItem
                    {
                        Content = voice,
                        Tag = voice
                    });
                }

                // select current voice
                string targetVoice = _config!.TtsVoice;
                int idx = 0;
                foreach (ComboBoxItem item in CbVoice.Items)
                {
                    if (item.Tag is string tag && tag == targetVoice)
                    {
                        CbVoice.SelectedIndex = idx;
                        break;
                    }
                    idx++;
                }
            });

            client.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not load voices for language '{langCode}': {ex.Message}");
        }
    }

    private void UpdateTtsStatus()
    {
        if (ChkTtsEnabled.IsChecked == true)
        {
            TbTtsStatus.Text = "Voice TTS is enabled - translations will be spoken aloud";
        }
        else
        {
            TbTtsStatus.Text = "Voice TTS is disabled";
        }
    }

    private void ChkTtsEnabled_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || _config is null) return;
        
        _config.TtsEnabled = true;
        _manager?.Save();
        UpdateTtsStatus();
        TtsSettingsChanged?.Invoke();
    }

    private void ChkTtsEnabled_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_loading || _config is null) return;
        
        _config.TtsEnabled = false;
        _manager?.Save();
        UpdateTtsStatus();
        TtsSettingsChanged?.Invoke();
    }

    private void CbOutputDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config is null || CbOutputDevice.SelectedItem is not ComboBoxItem item) return;
        
        if (item.Tag is int id)
        {
            _config.TtsOutputDeviceId = id;
            _manager?.Save();
            TtsSettingsChanged?.Invoke();
        }
    }

    private async void CbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config is null || CbLanguage.SelectedItem is not ComboBoxItem item) return;
        
        if (item.Tag is string langCode)
        {
            _config.TtsLanguage = langCode;
            _manager?.Save();
            await LoadVoicesForLanguageAsync(langCode);
            TtsSettingsChanged?.Invoke();
        }
    }

    private void CbVoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config is null || CbVoice.SelectedItem is not ComboBoxItem item) return;
        
        if (item.Tag is string voice)
        {
            _config.TtsVoice = voice;
            _manager?.Save();
            TtsSettingsChanged?.Invoke();
        }
    }

    private void CbRate_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config is null || CbRate.SelectedItem is not ComboBoxItem item) return;
        
        if (item.Tag is string rate)
        {
            _config.TtsRate = rate;
            _manager?.Save();
            TtsSettingsChanged?.Invoke();
        }
    }

    private void CbVolume_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config is null || CbVolume.SelectedItem is not ComboBoxItem item) return;
        
        if (item.Tag is string volume)
        {
            _config.TtsVolume = volume;
            _manager?.Save();
            TtsSettingsChanged?.Invoke();
        }
    }

    private void CbPitch_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config is null || CbPitch.SelectedItem is not ComboBoxItem item) return;
        
        if (item.Tag is string pitch)
        {
            _config.TtsPitch = pitch;
            _manager?.Save();
            TtsSettingsChanged?.Invoke();
        }
    }

    private async void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        BtnRefreshDevices.IsEnabled = false;
        TbTtsStatus.Text = "Refreshing audio devices...";

        await LoadOutputDevicesAsync();

        BtnRefreshDevices.IsEnabled = true;
        TbTtsStatus.Text = "Audio devices refreshed";
    }
}
