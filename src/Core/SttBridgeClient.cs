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
        CancellationToken ct = default)
    {
        var body = new
        {
            language                 = sourceLang,
            initial_silence_timeout  = initialSilenceTimeout,
            silence_timeout          = silenceTimeout,
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

    public async Task<bool> StopAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync($"{_baseUrl}/stop", null, ct);
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

    public void Dispose() => _http.Dispose();

    private record ModelsListResponse([property: JsonPropertyName("models")] string[] Models);
    private record DownloadStartResponse(
        [property: JsonPropertyName("ok")]      bool   Ok,
        [property: JsonPropertyName("message")] string Message);
}
