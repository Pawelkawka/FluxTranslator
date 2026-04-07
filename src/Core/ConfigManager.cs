using System.IO;
using System.Text.Json;

namespace FluxTranslator.Core;

public class ConfigManager
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented      = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _configPath;

    public AppConfig Config { get; private set; } = new AppConfig();

    public ConfigManager()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppSettings.AppName);

        try   { Directory.CreateDirectory(dir); }
        catch { dir = AppDomain.CurrentDomain.BaseDirectory; }

        _configPath = Path.Combine(dir, "settings.json");
        AppLogger.Info($"Config path: {_configPath}");
    }

    public void Load()
    {
        if (!File.Exists(_configPath))
        {
            AppLogger.Info("No config file found – using defaults.");
            Config = new AppConfig();
            return;
        }

        try
        {
            var json   = File.ReadAllText(_configPath);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, _jsonOpts);
            if (loaded is not null)
            {
                Config = loaded;
                Sanitize();
                AppLogger.Info("Config loaded successfully.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to load config: {ex.Message}");
            Config = new AppConfig();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Config, _jsonOpts);
            File.WriteAllText(_configPath, json);
            AppLogger.Info("Config saved.");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to save config: {ex.Message}");
        }
    }

    private void Sanitize()
    {
        Config.BackgroundOpacity = Math.Clamp(Config.BackgroundOpacity, 0, 100);
        Config.FontSize          = Math.Clamp(Config.FontSize,          8, 72);
        Config.Padding           = Math.Clamp(Config.Padding,           0, 60);
        Config.CornerRadius      = Math.Clamp(Config.CornerRadius,      0, 40);
        Config.BorderWidth       = Math.Clamp(Config.BorderWidth,       0, 10);

        Config.OverlayDisplayTime    = Math.Clamp(Config.OverlayDisplayTime,    1, 120);
        Config.InitialSilenceTimeout = Math.Clamp(Config.InitialSilenceTimeout, 1.0, 30.0);
        Config.SilenceTimeout        = Math.Clamp(Config.SilenceTimeout,        0.05, 5.0);

        if (string.IsNullOrWhiteSpace(Config.LibreTranslateUrl))
            Config.LibreTranslateUrl = AppSettings.DefaultLibreTranslateUrl;

        if (!AppSettings.SourceLanguages.ContainsKey(Config.SourceLanguage))
            Config.SourceLanguage = AppSettings.DefaultSourceLanguage;

        if (!AppSettings.TargetLanguages.ContainsKey(Config.TargetLanguage))
            Config.TargetLanguage = AppSettings.DefaultTargetLanguage;

        if (!AppSettings.OverlayPositions.ContainsKey(Config.OverlayPosition))
            Config.OverlayPosition = AppSettings.DefaultOverlayPosition;
    }
}
