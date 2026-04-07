using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluxTranslator.Core;

namespace FluxTranslator.Tabs;

public partial class AppearanceTab : UserControl
{
    private AppConfig     _config  = null!;
    private ConfigManager _manager = null!;
    private bool          _loading;
    private string        _activeColorTag = "";

    public event Action? ConfigChanged;

    public AppearanceTab()
    {
        InitializeComponent();
        Picker.ColorPicked += Picker_ColorPicked;
    }

    public void Initialise(AppConfig config, ConfigManager manager)
    {
        _config  = config;
        _manager = manager;
        LoadFonts();
        LoadValues();
    }

    public void Refresh()
    {
        if (_config is null) return;
        LoadValues();
    }

    private void LoadFonts()
    {
        CbFont.Items.Clear();
        foreach (var family in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
            CbFont.Items.Add(family.Source);
    }

    private void LoadValues()
    {
        _loading = true;

        CbPosition.Items.Clear();
        int posIdx = 0, i = 0;
        foreach (var (key, label) in AppSettings.OverlayPositions)
        {
            CbPosition.Items.Add(new ComboBoxItem { Content = label, Tag = key });
            if (key == _config.OverlayPosition) posIdx = i;
            i++;
        }
        CbPosition.SelectedIndex = posIdx;

        var fontSrc = _config.FontFamily;
        var match   = CbFont.Items.Cast<string>().FirstOrDefault(f =>
            f.Equals(fontSrc, StringComparison.OrdinalIgnoreCase));
        CbFont.SelectedItem = match ?? (CbFont.Items.Count > 0 ? CbFont.Items[0] : null);

        SetSlider(SlFontSize,    LblFontSize,    _config.FontSize);
        SetSlider(SlBgOpacity,   LblBgOpacity,   _config.BackgroundOpacity);
        SetSlider(SlPadding,     LblPadding,     _config.Padding);
        SetSlider(SlCorner,      LblCorner,      _config.CornerRadius);
        SetSlider(SlBorderWidth, LblBorderWidth, _config.BorderWidth);

        ChkBold.IsChecked  = _config.FontBold;

        SetSwatch(PrvText,   _config.TextColor);
        SetSwatch(PrvBg,     _config.BackgroundColor);
        SetSwatch(PrvBorder, _config.BorderColor);

        _loading = false;

        UpdatePreview();
    }

    private static void SetSlider(Slider sl, TextBlock lbl, double value)
    {
        sl.Value  = Math.Clamp(value, sl.Minimum, sl.Maximum);
        lbl.Text  = ((int)sl.Value).ToString();
    }

    private void CbPosition_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config is null) return;
        if (CbPosition.SelectedItem is ComboBoxItem item && item.Tag is string key)
        { _config.OverlayPosition = key; Save(); }
    }

    private void CbFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config is null) return;
        if (CbFont.SelectedItem is string font)
        { _config.FontFamily = font; Save(); }
    }

    private void SlFontSize_ValueChanged(object s, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _config is null) return;
        _config.FontSize  = (int)SlFontSize.Value;
        LblFontSize.Text  = _config.FontSize.ToString();
        Save();
    }

    private void ChkBold_Changed(object s, System.Windows.RoutedEventArgs e)
    {
        if (_loading || _config is null) return;
        _config.FontBold = ChkBold.IsChecked == true; Save();
    }

    private void SlBgOpacity_ValueChanged(object s, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _config is null) return;
        _config.BackgroundOpacity = (int)SlBgOpacity.Value;
        LblBgOpacity.Text = _config.BackgroundOpacity.ToString(); Save();
    }

    private void SlPadding_ValueChanged(object s, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _config is null) return;
        _config.Padding  = (int)SlPadding.Value;
        LblPadding.Text  = _config.Padding.ToString(); Save();
    }

    private void SlCorner_ValueChanged(object s, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _config is null) return;
        _config.CornerRadius = (int)SlCorner.Value;
        LblCorner.Text       = _config.CornerRadius.ToString(); Save();
    }

    private void SlBorderWidth_ValueChanged(object s, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _config is null) return;
        _config.BorderWidth  = (int)SlBorderWidth.Value;
        LblBorderWidth.Text  = _config.BorderWidth.ToString(); Save();
    }



    private void Save()
    {
        _manager.Save();
        UpdatePreview();
        ConfigChanged?.Invoke();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;
        _config.FontFamily        = AppSettings.DefaultFontFamily;
        _config.FontSize          = AppSettings.DefaultFontSize;
        _config.FontBold          = AppSettings.DefaultFontBold;
        _config.TextColor         = AppSettings.DefaultTextColor;
        _config.BackgroundColor   = AppSettings.DefaultBackgroundColor;
        _config.BackgroundOpacity = AppSettings.DefaultBackgroundOpacity;
        _config.Padding           = AppSettings.DefaultPadding;
        _config.CornerRadius      = AppSettings.DefaultCornerRadius;
        _config.BorderWidth       = AppSettings.DefaultBorderWidth;
        _config.BorderColor       = AppSettings.DefaultBorderColor;
        _config.OverlayPosition   = AppSettings.DefaultOverlayPosition;
        Save();
        LoadValues();
    }

    private static bool IsValidHex(string s) =>
        !string.IsNullOrWhiteSpace(s) && s.StartsWith('#') &&
        (s.Length == 7 || s.Length == 9);

    private void PrvText_Click(object s, MouseButtonEventArgs e)
        => OpenPicker("text", _config.TextColor, (UIElement)s);

    private void PrvBg_Click(object s, MouseButtonEventArgs e)
        => OpenPicker("bg", _config.BackgroundColor, (UIElement)s);

    private void PrvBorder_Click(object s, MouseButtonEventArgs e)
        => OpenPicker("border", _config.BorderColor, (UIElement)s);

    private void OpenPicker(string tag, string hex, UIElement target)
    {
        if (_config is null) return;

        if (ColorPickerPopup.IsOpen && _activeColorTag == tag)
        {
            ColorPickerPopup.IsOpen = false;
            return;
        }

        _activeColorTag = tag;
        Picker.SetHex(hex);
        ColorPickerPopup.PlacementTarget = target;
        ColorPickerPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ColorPickerPopup.IsOpen = true;
    }

    private void Appearance_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ColorPickerPopup.IsOpen) return;
        var child = ColorPickerPopup.Child as UIElement;
        if (child != null && child.IsMouseOver) return;
        ColorPickerPopup.IsOpen = false;
    }

    private void Appearance_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ColorPickerPopup.IsOpen)
        {
            ColorPickerPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    public void Picker_ColorPicked(string hex)
    {
        ColorPickerPopup.IsOpen = false;
        if (_config is null) return;
        switch (_activeColorTag)
        {
            case "text":
                _config.TextColor = hex;
                SetSwatch(PrvText, hex);
                break;
            case "bg":
                _config.BackgroundColor = hex;
                SetSwatch(PrvBg, hex);
                break;
            case "border":
                _config.BorderColor = hex;
                SetSwatch(PrvBorder, hex);
                break;
        }
        Save();
    }

    private static void SetSwatch(System.Windows.Controls.Border swatch, string hex)
    {
        var c = TryParseColor(hex);
        if (c is not null)
            swatch.Background = new SolidColorBrush(c.Value);
    }

    private static System.Windows.Media.Color? TryParseColor(string hex)
    {
        try
        {
            return (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch { return null; }
    }

    private void UpdatePreview()
    {
        if (PreviewOverlay is null || PreviewText is null || _config is null) return;

        var bgColor = TryParseColor(_config.BackgroundColor)
                      ?? System.Windows.Media.Color.FromRgb(0, 0, 0);
        bgColor.A = (byte)(Math.Clamp(_config.BackgroundOpacity, 0, 100) / 100.0 * 255);
        PreviewOverlay.Background       = new SolidColorBrush(bgColor);
        PreviewOverlay.CornerRadius      = new CornerRadius(_config.CornerRadius);
        PreviewOverlay.Padding           = new Thickness(_config.Padding);

        var borderColor = TryParseColor(_config.BorderColor)
                          ?? System.Windows.Media.Color.FromRgb(64, 64, 64);
        PreviewOverlay.BorderBrush     = new SolidColorBrush(borderColor);
        PreviewOverlay.BorderThickness = new Thickness(_config.BorderWidth);

        PreviewText.FontSize   = Math.Clamp(_config.FontSize, 8, 72);
        PreviewText.FontFamily = new FontFamily(_config.FontFamily);
        PreviewText.FontWeight = _config.FontBold ? FontWeights.Bold : FontWeights.Normal;

        var textColor = TryParseColor(_config.TextColor)
                        ?? System.Windows.Media.Color.FromRgb(255, 255, 255);
        PreviewText.Foreground = new SolidColorBrush(textColor);
    }
}
