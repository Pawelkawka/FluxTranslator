using System.Diagnostics;
using System.IO;

namespace FluxTranslator.Core;

public record StatusEvent(string Text, bool IsError, bool IsFinal, int DurationMs = 0);

public class AppController : IDisposable
{
    public event Action<StatusEvent>? StatusChanged;
    public event Action<string>?      TranslationReady;
    public event Action?              BackendReady;

    private readonly ConfigManager _configManager;
    private readonly SttBridgeClient _sttBridgeClient;
    private readonly TranslationService _translationService;
    private readonly TtsController _ttsController;

    private bool   _isListening;
    private string _lastSttState = "idle";
    private string _lastTranslation = string.Empty;
    private CancellationTokenSource? _pollCts;

    public bool IsListening => _isListening;
    public string LastTranslation => _lastTranslation;
    public TtsController Tts => _ttsController;

    public AppController(ConfigManager cfg)
    {
        _configManager = cfg;
        _sttBridgeClient = new SttBridgeClient(AppSettings.SttPort);
        _translationService = new TranslationService();
        _ttsController = new TtsController(cfg);

        TranslationReady += OnTranslationReady;
        
        // Start backend health monitoring
        _ = MonitorBackendHealthAsync();
    }

    private async Task MonitorBackendHealthAsync()
    {
        while (true)
        {
            try
            {
                var isReady = await _sttBridgeClient.WaitUntilReadyAsync(2000);
                if (isReady)
                {
                    AppLogger.Info("STT backend is ready.");
                    BackendReady?.Invoke();
                    break;
                }
            }
            catch
            {
                // Backend not available yet, continue monitoring
            }

            AppLogger.Debug("Waiting for STT backend to become available...");
            await Task.Delay(5000);
        }
    }

    public async Task ToggleListeningAsync()
    {
        if (_isListening)
        {
            await StopListeningAsync(finalizeRecording: _configManager.Config.EnableManualMode);
        }
        else
        {
            await StartListeningAsync();
        }
    }

    public async Task StartListeningAsync()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();

        _isListening  = true;
        _lastSttState = "starting";

        Emit("Calibrating microphone…", false, false);

        var config = _configManager.Config;
        bool ok = await _sttBridgeClient.StartAsync(
            config.SourceLanguage,
            config.InitialSilenceTimeout,
            config.SilenceTimeout,
            config.MaxRecordingSeconds,
            config.EnableManualMode);

        if (!ok)
        {
            _isListening = false;
            Emit("Could not connect to STT backend.", true, true);
            return;
        }

        _ = Task.Run(() => PollLoopAsync(_pollCts.Token));
    }

    public async Task StopListeningAsync(bool finalizeRecording = false)
    {
        if (!finalizeRecording)
        {
            _pollCts?.Cancel();
        }

        _isListening = false;
        await _sttBridgeClient.StopAsync(finalizeRecording);
    }

    public async Task StopAllAsync()
    {
        _pollCts?.Cancel();
        if (_isListening)
        {
            _isListening = false;
            await _sttBridgeClient.StopAsync();
        }
        _isListening  = false;
        _lastSttState = "idle";
        await _ttsController.StopAsync();
    }

    public void CopyLastTranslation()
    {
        if (string.IsNullOrEmpty(_lastTranslation))
        {
            Emit("No text to copy.", false, true, 2000);
            return;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.Clipboard.SetText(_lastTranslation));

        Emit("Copied to clipboard!", false, true, 2000);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var maxWaitMs = (int)TimeSpan.FromSeconds(
            _configManager.Config.MaxRecordingSeconds
            + _configManager.Config.InitialSilenceTimeout
            + 20.0
        ).TotalMilliseconds;
        var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            try
            {
                var status = await _sttBridgeClient.GetStatusAsync(ct);
                if (status is null) { await Task.Delay(300, ct); continue; }

                if (status.State != _lastSttState)
                {
                    _lastSttState = status.State;
                    AppLogger.Debug($"STT state → {status.State}: {status.Text}");

                    switch (status.State)
                    {
                        case "done" when !string.IsNullOrWhiteSpace(status.Text):
                            _ = Task.Run(() => RunTranslationAsync(status.Text, ct), ct);
                            _isListening = false;
                            return;

                        case "error":
                            Emit(status.Text, true, true);
                            _isListening = false;
                            return;

                        case "idle" when status.IsFinal:
                            _isListening = false;
                            return;

                        default:
                            if (!status.IsFinal)
                                Emit(status.Text, false, false);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { AppLogger.Error($"Poll loop error: {ex.Message}"); }

            await Task.Delay(200, ct);
        }

        if (!ct.IsCancellationRequested && DateTime.UtcNow >= deadline)
        {
            AppLogger.Warn("STT polling timed out; stopping active recording.");
            await _sttBridgeClient.StopAsync(finalizeRecording: true);
            Emit("Recording timed out.", true, true);
        }

        _isListening = false;
    }

    private async Task RunTranslationAsync(string recognizedText, CancellationToken ct)
    {
        var config = _configManager.Config;
        Emit("Translating…", false, false);

        (bool ok, string result) =
            config.TranslationEngine == TranslationEngine.CTranslate2
                ? await _translationService.TranslateCTranslate2Async(
                    recognizedText,
                    config.SourceLanguage,
                    config.TargetLanguage,
                    ResolveModelsDir(config.CTranslate2ModelsDir),
                    AppSettings.SttPort,
                    ct)
                : await _translationService.TranslateAsync(
                    recognizedText,
                    config.SourceLanguage,
                    config.TargetLanguage,
                    config.LibreTranslateUrl,
                    ct);

        _lastTranslation = ok ? result : string.Empty;
        Emit(result, !ok, true);
        if (ok) TranslationReady?.Invoke(result);
    }

    private async void OnTranslationReady(string translation)
    {
        try
        {
            await _ttsController.SpeakAsync(translation);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"TTS failed to speak translation: {ex.Message}");
        }
    }

    private void Emit(string text, bool isError, bool isFinal, int durationMs = 0)
    {
        StatusChanged?.Invoke(new StatusEvent(text, isError, isFinal, durationMs));
    }
    private static string ResolveModelsDir(string? path)
    {
        path = string.IsNullOrWhiteSpace(path)
            ? AppSettings.DefaultCTranslate2ModelsDir
            : path.Trim().Trim('"');

        if (Path.IsPathRooted(path)) return path;
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
    }

    public async Task<string[]> GetModelsAsync(CancellationToken ct = default)
    {
        var modelsDir = ResolveModelsDir(_configManager.Config.CTranslate2ModelsDir);
        var local = ScanLocalModels(modelsDir);
        if (local.Length > 0) return local;
        return await _sttBridgeClient.ListModelsAsync(modelsDir, ct);
    }

    private static string[] ScanLocalModels(string modelsDir)
    {
        if (!Directory.Exists(modelsDir)) return [];
        return Directory.GetDirectories(modelsDir)
            .Where(d => File.Exists(Path.Combine(d, "model.bin"))
                     || File.Exists(Path.Combine(d, "config.json"))
                     || Directory.GetFiles(d, "*.bin").Length > 0)
            .Select(d => Path.GetFileName(d)!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<(bool ok, string message)> StartModelDownloadAsync(
        string modelName, CancellationToken ct = default)
    {
        var modelsDir = ResolveModelsDir(_configManager.Config.CTranslate2ModelsDir);
        return await _sttBridgeClient.StartModelDownloadAsync(modelName, modelsDir, ct);
    }

    public async Task<ModelDownloadStatus?> GetModelDownloadStatusAsync(CancellationToken ct = default)
        => await _sttBridgeClient.GetModelDownloadStatusAsync(ct);
    
    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _sttBridgeClient.Dispose();
        _translationService.Dispose();
        _ttsController.Dispose();
    }
}
