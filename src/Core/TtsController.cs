using System;
using System.Threading;
using System.Threading.Tasks;

namespace FluxTranslator.Core;

public class TtsController : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly SttBridgeClient _sttBridgeClient;
    private readonly SemaphoreSlim _speakLock = new(1, 1);
    private bool _disposed;

    public TtsController(ConfigManager configManager)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _sttBridgeClient = new SttBridgeClient(AppSettings.SttPort);
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (_disposed) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        var config = _configManager.Config;
        if (!config.TtsEnabled) return;

        await _speakLock.WaitAsync(ct);
        try
        {
            if (_disposed) return;

            // Determine language
            string language = config.TtsLanguage;
            if (string.IsNullOrEmpty(language))
            {
                language = "en"; // fallback
            }

            // Determine voice - use selected voice or get first voice for language
            string voice = config.TtsVoice;
            if (string.IsNullOrEmpty(voice))
            {
                var (ok, autoVoice) = await _sttBridgeClient.GetAutoVoiceAsync(language, ct);
                if (ok)
                {
                    voice = autoVoice;
                }
                else
                {
                    AppLogger.Warn("Could not get voice for language, using default");
                    voice = "en-US-EmmaMultilingualNeural";
                }
            }

            int? deviceId = config.TtsOutputDeviceId == -1 ? null : config.TtsOutputDeviceId;

            AppLogger.Info($"TTS speaking: voice={voice}, text={text[..Math.Min(50, text.Length)]}");

            bool success = await _sttBridgeClient.SpeakAsync(
                text,
                voice,
                deviceId,
                config.TtsRate,
                config.TtsVolume,
                config.TtsPitch,
                ct);

            if (!success)
            {
                AppLogger.Warn("TTS SpeakAsync returned false");
            }
        }
        finally
        {
            _speakLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_disposed) return;
        
        try
        {
            await _sttBridgeClient.StopSpeakingAsync(ct);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Error stopping TTS: {ex.Message}");
        }
    }

    public async Task<bool> IsSpeakingAsync(CancellationToken ct = default)
    {
        if (_disposed) return false;
        
        try
        {
            return await _sttBridgeClient.IsSpeakingAsync(ct);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Error checking TTS status: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _speakLock.Dispose();
        _sttBridgeClient.Dispose();
        
        AppLogger.Info("TTS controller disposed.");
    }
}
