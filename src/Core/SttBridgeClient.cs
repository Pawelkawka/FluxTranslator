using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxTranslator.Core;

public record SttStatus(
    [property: JsonPropertyName("state")]    string State,
    [property: JsonPropertyName("text")]     string Text,
    [property: JsonPropertyName("is_error")] bool   IsError,
    [property: JsonPropertyName("is_final")] bool   IsFinal
);

public record ModelDownloadStatus(
    [property: JsonPropertyName("active")]   bool    Active,
    [property: JsonPropertyName("model")]    string  Model,
    [property: JsonPropertyName("progress")] string  Progress,
    [property: JsonPropertyName("success")]  bool?   Success,
    [property: JsonPropertyName("error")]    string  Error
);

public class SttBridgeClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string     _baseUrl;

    public SttBridgeClient(int port = AppSettings.SttPort)
    {
        _baseUrl = $"http://127.0.0.1:{port}";
        _http    = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<bool> StartAsync(
        string sourceLang,
        double initialSilenceTimeout,
        double silenceTimeout,
        int maxRecordingSeconds = AppSettings.DefaultMaxRecordingSeconds,
        bool manualMode = AppSettings.DefaultEnableManualMode,
        CancellationToken ct = default)
    {
        var body = new
        {
            language                 = sourceLang,
            initial_silence_timeout  = initialSilenceTimeout,
            silence_timeout          = silenceTimeout,
            max_recording_seconds    = maxRecordingSeconds,
            manual_mode              = manualMode,
        };

        try
        {
            var resp = await _http.PostAsJsonAsync($"{_baseUrl}/start", body, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.Start error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StopAsync(bool finalizeRecording = false, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"{_baseUrl}/stop",
                new { finalize_recording = finalizeRecording },
                ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.Stop error: {ex.Message}");
            return false;
        }
    }

    public async Task<SttStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<SttStatus>($"{_baseUrl}/status", ct);
        }
        catch (TaskCanceledException) { return null; }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.GetStatus error: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> WaitUntilReadyAsync(int timeoutMs = 8000, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var resp = await _http.GetAsync($"{_baseUrl}/health", ct);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }

            await Task.Delay(300, ct);
        }
        return false;
    }

    public async Task<string[]> ListModelsAsync(string modelsDir, CancellationToken ct = default)
    {
        try
        {
            var url  = $"{_baseUrl}/models?models_dir={Uri.EscapeDataString(modelsDir)}";
            var resp = await _http.GetFromJsonAsync<ModelsListResponse>(url, ct);
            return resp?.Models ?? [];
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.ListModels error: {ex.Message}");
            return [];
        }
    }

    public async Task<(bool ok, string message)> StartModelDownloadAsync(
        string modelName, string modelsDir, CancellationToken ct = default)
    {
        var body = new { model_name = modelName, models_dir = modelsDir };
        try
        {
            var resp = await _http.PostAsJsonAsync($"{_baseUrl}/models/download", body, ct);
            var json = await resp.Content.ReadFromJsonAsync<DownloadStartResponse>(ct);
            return (json?.Ok ?? false, json?.Message ?? "Unknown error");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.StartModelDownload error: {ex.Message}");
            return (false, ex.Message);
        }
    }

    public async Task<ModelDownloadStatus?> GetModelDownloadStatusAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<ModelDownloadStatus>(
                $"{_baseUrl}/models/download/status", ct);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.GetModelDownloadStatus error: {ex.Message}");
            return null;
        }
    }

    //tts methods

    public async Task<TtsVoiceInfo[]> ListVoicesAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<TtsVoicesResponse>($"{_baseUrl}/tts/voices", ct);
            return resp?.Voices ?? [];
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.ListVoices error: {ex.Message}");
            return [];
        }
    }

    public async Task<TtsLanguageInfo[]> ListLanguagesAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<TtsLanguagesResponse>($"{_baseUrl}/tts/languages", ct);
            return resp?.Languages ?? [];
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.ListLanguages error: {ex.Message}");
            return [];
        }
    }

    public async Task<(bool ok, string voice)> GetAutoVoiceAsync(string targetLanguage, CancellationToken ct = default)
    {
        var body = new { target_language = targetLanguage };
        try
        {
            var resp = await _http.PostAsJsonAsync($"{_baseUrl}/tts/voice/auto", body, ct);
            var json = await resp.Content.ReadFromJsonAsync<TtsAutoVoiceResponse>(ct);
            return (json?.Ok ?? false, json?.Voice ?? string.Empty);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.GetAutoVoice error: {ex.Message}");
            return (false, string.Empty);
        }
    }

    public async Task<bool> SpeakAsync(
        string text,
        string voice,
        string? deviceId = null,
        string rate = "+0%",
        string volume = "+0%",
        string pitch = "+0Hz",
        CancellationToken ct = default)
    {
        var body = new
        {
            text,
            voice,
            device_id = deviceId,
            rate,
            volume,
            pitch,
        };

        try
        {
            var resp = await _http.PostAsJsonAsync($"{_baseUrl}/tts/speak", body, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.Speak error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StopSpeakingAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync($"{_baseUrl}/tts/stop", null, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.StopSpeaking error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> IsSpeakingAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<TtsStatusResponse>($"{_baseUrl}/tts/status", ct);
            return resp?.Speaking ?? false;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.IsSpeaking error: {ex.Message}");
            return false;
        }
    }

    public async Task<TtsDeviceInfo[]> ListDevicesAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<TtsDevicesResponse>($"{_baseUrl}/tts/devices", ct);
            return resp?.Devices ?? [];
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SttBridge.ListDevices error: {ex.Message}");
            return [];
        }
    }

    private record ModelsListResponse([property: JsonPropertyName("models")] string[] Models);
    private record DownloadStartResponse(
        [property: JsonPropertyName("ok")]      bool   Ok,
        [property: JsonPropertyName("message")] string Message);
    private record TtsVoicesResponse([property: JsonPropertyName("voices")] TtsVoiceInfo[] Voices);
    private record TtsLanguagesResponse([property: JsonPropertyName("languages")] TtsLanguageInfo[] Languages);
    private record TtsAutoVoiceResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("voice")] string Voice);
    private record TtsStatusResponse([property: JsonPropertyName("speaking")] bool Speaking);
    private record TtsDevicesResponse([property: JsonPropertyName("devices")] TtsDeviceInfo[] Devices);

    public void Dispose() => _http.Dispose();
}

public record TtsVoiceInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("locale")] string Locale,
    [property: JsonPropertyName("gender")] string Gender,
    [property: JsonPropertyName("friendly_name")] string FriendlyName);

public record TtsLanguageInfo(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("voices")] string[] Voices);

public record TtsDeviceInfo(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("channels")] int Channels,
    [property: JsonPropertyName("sample_rate")] double? SampleRate,
    [property: JsonPropertyName("is_default")] bool IsDefault);
