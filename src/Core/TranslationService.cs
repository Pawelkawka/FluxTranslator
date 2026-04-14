using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FluxTranslator.Core;

file record LibreTranslateResponse(
    [property: JsonPropertyName("translatedText")] string TranslatedText
);

file record Ct2TranslateRequest(
    [property: JsonPropertyName("text")]        string Text,
    [property: JsonPropertyName("source_lang")] string SourceLang,
    [property: JsonPropertyName("target_lang")] string TargetLang,
    [property: JsonPropertyName("models_dir")]  string ModelsDir
);

file record Ct2TranslateResponse(
    [property: JsonPropertyName("ok")]     bool   Ok,
    [property: JsonPropertyName("result")] string? Result,
    [property: JsonPropertyName("error")]  string? Error
);

public class TranslationService : IDisposable
{
    private readonly HttpClient _http;

    public TranslationService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<(bool ok, string result)> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        string serverUrl,
        CancellationToken ct = default)
    {
        var src = NormalizeLangCode(sourceLang);
        var tgt = NormalizeLangCode(targetLang);

        if (src == tgt)
        {
            AppLogger.Warn("Source and target languages are the same / skipping translation.");
            return (false, "Source and target languages are the same.");
        }

        var payload = new { q = text, source = src, target = tgt, api_key = "" };
        int   delay    = 1000;
        const int retries = 3;

        for (int i = 0; i < retries; i++)
        {
            try
            {
                var resp = await _http.PostAsJsonAsync(serverUrl, payload, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var body       = await resp.Content.ReadFromJsonAsync<LibreTranslateResponse>(ct);
                    var translated = body?.TranslatedText ?? string.Empty;
                    AppLogger.Info($"[LibreTranslate] Translated: \"{translated}\"");
                    return (true, translated);
                }
                AppLogger.Warn($"LibreTranslate HTTP {(int)resp.StatusCode} on attempt {i + 1}");
            }
            catch (TaskCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLogger.Error($"LibreTranslate attempt {i + 1}: {ex.Message}");
            }

            if (i < retries - 1)
                await Task.Delay(delay, ct);
            delay *= 2;
        }

        return (false, "Translation failed - check the LibreTranslate server.");
    }

    public async Task<(bool ok, string result)> TranslateCTranslate2Async(
        string text,
        string sourceLang,
        string targetLang,
        string modelsDir,
        int    port = AppSettings.SttPort,
        CancellationToken ct = default)
    {
        var src = NormalizeLangCode(sourceLang);
        var tgt = NormalizeLangCode(targetLang);

        if (src == tgt)
        {
            AppLogger.Warn("Source and target languages are the same / skipping translation.");
            return (false, "Source and target languages are the same.");
        }

        var baseUrl = $"http://127.0.0.1:{port}";
        var req = new Ct2TranslateRequest(text, src, tgt, modelsDir);

        try
        {
            var resp = await _http.PostAsJsonAsync($"{baseUrl}/translate", req, ct);
            var body = await resp.Content.ReadFromJsonAsync<Ct2TranslateResponse>(ct);

            if (resp.IsSuccessStatusCode && body?.Ok == true)
            {
                var translated = body.Result ?? string.Empty;
                AppLogger.Info($"[CTranslate2] Translated: \"{translated}\"");
                return (true, translated);
            }

            var err = body?.Error ?? $"HTTP {(int)resp.StatusCode}";
            AppLogger.Warn($"CTranslate2 translation failed: {err}");
            return (false, err);
        }
        catch (TaskCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.Error($"CTranslate2 translate error: {ex.Message}");
            return (false, $"CTranslate2 error: {ex.Message}");
        }
    }

    public static string NormalizeLangCode(string code)
    {
        var dash = code.IndexOf('-');
        return dash >= 0 ? code[..dash].ToLowerInvariant() : code.ToLowerInvariant();
    }

    public void Dispose() => _http.Dispose();
}
