using System;
using System.Collections.Generic;
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
        bool configChanged = false;

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
        configChanged |= await LoadLanguagesAsync();

        // set current values
        var selectedRate = SelectComboBoxItemByTag(CbRate, _config.TtsRate, AppSettings.DefaultTtsRate);
        if (_config.TtsRate != selectedRate)
        {
            _config.TtsRate = selectedRate;
            configChanged = true;
        }

        var selectedVolume = SelectComboBoxItemByTag(CbVolume, _config.TtsVolume, AppSettings.DefaultTtsVolume);
        if (_config.TtsVolume != selectedVolume)
        {
            _config.TtsVolume = selectedVolume;
            configChanged = true;
        }

        var selectedPitch = SelectComboBoxItemByTag(CbPitch, _config.TtsPitch, AppSettings.DefaultTtsPitch);
        if (_config.TtsPitch != selectedPitch)
        {
            _config.TtsPitch = selectedPitch;
            configChanged = true;
        }

        if (configChanged)
            _manager?.Save();

        _loading = false;
    }

    private void LoadRateOptions()
    {
        CbRate.Items.Clear();
        var rates = new[]
        {
            (Label: "-50%", Value: "-50%"),
            (Label: "-30%", Value: "-30%"),
            (Label: "-20%", Value: "-20%"),
            (Label: "-10%", Value: "-10%"),
            (Label: "0", Value: "+0%"),
            (Label: "+10%", Value: "+10%"),
            (Label: "+20%", Value: "+20%"),
            (Label: "+30%", Value: "+30%"),
            (Label: "+50%", Value: "+50%")
        };

        foreach (var (label, value) in rates)
        {
            CbRate.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }
    }

    private void LoadVolumeOptions()
    {
        CbVolume.Items.Clear();
        var volumes = new[]
        {
            (Label: "-50%", Value: "-50%"),
            (Label: "-30%", Value: "-30%"),
            (Label: "-20%", Value: "-20%"),
            (Label: "-10%", Value: "-10%"),
            (Label: "0%", Value: "+0%"),
            (Label: "+10%", Value: "+10%"),
            (Label: "+20%", Value: "+20%"),
            (Label: "+30%", Value: "+30%"),
            (Label: "+50%", Value: "+50%")
        };

        foreach (var (label, value) in volumes)
        {
            CbVolume.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }
    }

    private void LoadPitchOptions()
    {
        CbPitch.Items.Clear();
        var pitches = new[]
        {
            (Label: "-20 Hz", Value: "-20Hz"),
            (Label: "-10 Hz", Value: "-10Hz"),
            (Label: "0 Hz", Value: "+0Hz"),
            (Label: "+10 Hz", Value: "+10Hz"),
            (Label: "+20 Hz", Value: "+20Hz")
        };

        foreach (var (label, value) in pitches)
        {
            CbPitch.Items.Add(new ComboBoxItem { Content = label, Tag = value });
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
            using var client = new SttBridgeClient(AppSettings.SttPort);
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
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not load TTS devices: {ex.Message}");
        }
    }

    private async Task<bool> LoadLanguagesAsync()
    {
        CbLanguage.Items.Clear();
        bool configChanged = false;

        TtsLanguageInfo[]? cachedLanguages = null;
        try
        {
            using var client = new SttBridgeClient(AppSettings.SttPort);
            cachedLanguages = await client.ListLanguagesAsync().ConfigureAwait(false);

            // Switch back to UI thread for updating controls
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var language in cachedLanguages.OrderBy(l => l.Name))
                {
                    CbLanguage.Items.Add(new ComboBoxItem
                    {
                        Content = language.Name,
                        Tag = language.Code
                    });
                }

                // select current language
                string selectedLanguage = SelectComboBoxItemByTag(CbLanguage, _config!.TtsLanguage, AppSettings.DefaultTtsLanguage);
                if (!string.IsNullOrWhiteSpace(selectedLanguage) && _config.TtsLanguage != selectedLanguage)
                {
                    _config.TtsLanguage = selectedLanguage;
                    configChanged = true;
                }
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not load TTS languages: {ex.Message}");
        }

        configChanged |= await LoadVoicesForLanguageAsync(_config!.TtsLanguage, cachedLanguages);
        return configChanged;
    }

    private async Task<bool> LoadVoicesForLanguageAsync(string langCode, TtsLanguageInfo[]? cachedLanguages = null)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CbVoice.Items.Clear();
        });

        if (string.IsNullOrEmpty(langCode)) return false;

        bool configChanged = false;

        try
        {
            TtsLanguageInfo[] languages;
            if (cachedLanguages is not null)
            {
                languages = cachedLanguages;
            }
            else
            {
                using var client = new SttBridgeClient(AppSettings.SttPort);
                languages = await client.ListLanguagesAsync().ConfigureAwait(false);
            }

            var lang = languages.FirstOrDefault(l => l.Code == langCode);
            if (lang == null)
            {
                AppLogger.Warn($"Language '{langCode}' not found in TTS languages list");
                return false;
            }

            var voices = lang.Voices;
            string selectedVoice = string.Empty;

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

                string targetVoice = GetPreferredVoice(langCode, voices);
                selectedVoice = SelectComboBoxItemByTag(CbVoice, targetVoice);
            });

            if (RememberVoiceSelection(langCode, selectedVoice))
                configChanged = true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not load voices for language '{langCode}': {ex.Message}");
        }

        return configChanged;
    }

    private string GetPreferredVoice(string langCode, IEnumerable<string> availableVoices)
    {
        if (_config is null)
            return string.Empty;

        var voiceSet = availableVoices
            .Where(voice => !string.IsNullOrWhiteSpace(voice))
            .ToHashSet(StringComparer.Ordinal);

        if (_config.TtsVoiceSelections.TryGetValue(langCode, out var savedVoice) && voiceSet.Contains(savedVoice))
            return savedVoice;

        if (!string.IsNullOrWhiteSpace(_config.TtsVoice) && voiceSet.Contains(_config.TtsVoice))
            return _config.TtsVoice;

        return availableVoices.FirstOrDefault() ?? string.Empty;
    }

    private bool RememberVoiceSelection(string langCode, string voice)
    {
        if (_config is null || string.IsNullOrWhiteSpace(langCode) || string.IsNullOrWhiteSpace(voice))
            return false;

        _config.TtsVoiceSelections ??= [];
        bool hasSavedVoice = _config.TtsVoiceSelections.TryGetValue(langCode, out var savedVoice);
        bool changed = _config.TtsLanguage != langCode
            || _config.TtsVoice != voice
            || !hasSavedVoice
            || !string.Equals(savedVoice, voice, StringComparison.Ordinal);

        _config.TtsLanguage = langCode;
        _config.TtsVoice = voice;
        _config.TtsVoiceSelections[langCode] = voice;

        return changed;
    }

    private static string SelectComboBoxItemByTag(ComboBox comboBox, string? value, string? fallbackValue = null)
    {
        if (TrySelectComboBoxItemByTag(comboBox, value, out var selectedValue))
            return selectedValue;

        if (TrySelectComboBoxItemByTag(comboBox, fallbackValue, out selectedValue))
            return selectedValue;

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
            return (comboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        }

        comboBox.SelectedIndex = -1;
        return string.Empty;
    }

    private static bool TrySelectComboBoxItemByTag(ComboBox comboBox, string? value, out string selectedValue)
    {
        selectedValue = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        int idx = 0;
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (item.Tag is string tag && string.Equals(tag, value, StringComparison.Ordinal))
            {
                comboBox.SelectedIndex = idx;
                selectedValue = tag;
                return true;
            }

            idx++;
        }

        return false;
    }

    private void UpdateTtsStatus()
    {
        if (ChkTtsEnabled.IsChecked == true)
        {
            TbTtsStatus.Text = "Voice TTS is enabled. Translations will be spoken aloud.";
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
            await LoadVoicesForLanguageAsync(langCode);
            _manager?.Save();
            TtsSettingsChanged?.Invoke();
        }
    }

    private void CbVoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config is null || CbVoice.SelectedItem is not ComboBoxItem item) return;
        
        if (item.Tag is string voice)
        {
            var langCode = (CbLanguage.SelectedItem as ComboBoxItem)?.Tag as string ?? _config.TtsLanguage;
            RememberVoiceSelection(langCode, voice);
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
