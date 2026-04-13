using System.Text.Json.Serialization;

namespace FluxTranslator.Core;

public class AppConfig
{
    public string HotkeyTranslate { get; set; } = AppSettings.DefaultHotkeyTranslate;
    public string HotkeyCopy      { get; set; } = AppSettings.DefaultHotkeyCopy;
    public string HotkeyKillAll   { get; set; } = AppSettings.DefaultHotkeyKillAll;

    public string           LibreTranslateUrl  { get; set; } = AppSettings.DefaultLibreTranslateUrl;
    public TranslationEngine TranslationEngine { get; set; } = AppSettings.DefaultTranslationEngine;
    public string           CTranslate2ModelsDir { get; set; } = AppSettings.DefaultCTranslate2ModelsDir;
    public string           SourceLanguage    { get; set; } = AppSettings.DefaultSourceLanguage;
    public string           TargetLanguage    { get; set; } = AppSettings.DefaultTargetLanguage;

    public string OverlayPosition { get; set; } = AppSettings.DefaultOverlayPosition;

    public int    FontSize          { get; set; } = AppSettings.DefaultFontSize;
    public string TextColor         { get; set; } = AppSettings.DefaultTextColor;
    public string BackgroundColor   { get; set; } = AppSettings.DefaultBackgroundColor;
    public int    BackgroundOpacity { get; set; } = AppSettings.DefaultBackgroundOpacity;
    public int    Padding           { get; set; } = AppSettings.DefaultPadding;
    public int    BorderWidth       { get; set; } = AppSettings.DefaultBorderWidth;
    public string BorderColor       { get; set; } = AppSettings.DefaultBorderColor;
    public int    CornerRadius      { get; set; } = AppSettings.DefaultCornerRadius;
    public string FontFamily        { get; set; } = AppSettings.DefaultFontFamily;
    public bool   FontBold          { get; set; } = AppSettings.DefaultFontBold;

    public int    OverlayDisplayTime   { get; set; } = AppSettings.DefaultOverlayDisplayTime;
    public double InitialSilenceTimeout{ get; set; } = AppSettings.DefaultInitialSilenceTimeout;
    public double SilenceTimeout       { get; set; } = AppSettings.DefaultSilenceTimeout;

    public bool EnableManualMode { get; set; } = AppSettings.DefaultEnableManualMode;

    // tts settings
    public bool  TtsEnabled        { get; set; } = AppSettings.DefaultTtsEnabled;
    public string TtsOutputDeviceId { get; set; } = AppSettings.DefaultTtsOutputDeviceId;
    public string TtsLanguage      { get; set; } = AppSettings.DefaultTtsLanguage;
    public string TtsVoice         { get; set; } = AppSettings.DefaultTtsVoice;
    public Dictionary<string, string> TtsVoiceSelections { get; set; } = [];
    public string TtsRate          { get; set; } = AppSettings.DefaultTtsRate;
    public string TtsVolume        { get; set; } = AppSettings.DefaultTtsVolume;
    public string TtsPitch         { get; set; } = AppSettings.DefaultTtsPitch;
}
