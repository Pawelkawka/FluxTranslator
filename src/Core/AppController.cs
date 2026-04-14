using System.Diagnostics;
using System.IO;

namespace FluxTranslator.Core;

public record StatusEvent(string Text, bool IsError, bool IsFinal, int DurationMs = 0);

public class AppController : IDisposable
{
    public event Action<StatusEvent>? StatusChanged;
    public event Action<string>?      TranslationReady;

    private readonly ConfigManager _configManager;
    private readonly SttBridgeClient _sttBridgeClient;
    private readonly TranslationService _translationService;
    private readonly TtsController _ttsController;

    private Process? _backendProcess;

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
    }

    public async Task StartBackendAsync()
    {
        if (!TryCreateBackendStartInfo(out var processStartInfo, out var backendLabel))
        {
            AppLogger.Warn("Backend not found: no FluxHelper executable or Python entry script present.");
            return;
        }

        AppLogger.Info($"Starting STT backend on port {AppSettings.SttPort} using {backendLabel}…");
        if (!StartBackendProcess(processStartInfo))
            return;

        bool ready = await _sttBridgeClient.WaitUntilReadyAsync(12000);
        if (ready)
            AppLogger.Info("STT backend is ready.");
        else
            AppLogger.Warn("STT backend did not become ready in time – continuing anyway.");
    }

    private bool TryCreateBackendStartInfo(out ProcessStartInfo processStartInfo, out string backendLabel)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var executableCandidates = new[]
        {
            Path.Combine(baseDir, "FluxHelper.exe"),
            Path.Combine(baseDir, "backend", "FluxHelper.exe"),
        };

        var scriptCandidates = new[]
        {
            Path.Combine(baseDir, "backend", "fluxhelper.py"),
            Path.Combine(baseDir, "FluxHelper", "fluxhelper.py"),
            Path.Combine(baseDir, "backend", "stt_server.py"),
        };

        var executablePath = executableCandidates.FirstOrDefault(File.Exists);
        if (executablePath is not null)
        {
            processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = AppSettings.SttPort.ToString(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            backendLabel = Path.GetFileName(executablePath);
            return true;
        }

        var scriptPath = scriptCandidates.FirstOrDefault(File.Exists);
        if (scriptPath is not null)
        {
            processStartInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\" {AppSettings.SttPort}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            backendLabel = Path.GetFileName(scriptPath);
            return true;
        }

        processStartInfo = new ProcessStartInfo();
        backendLabel = string.Empty;
        return false;
    }

    private bool StartBackendProcess(ProcessStartInfo processStartInfo)
    {
        try
        {
            _backendProcess = Process.Start(processStartInfo);
            if (_backendProcess is null)
            {
                AppLogger.Error("Could not start STT backend process.");
                return false;
            }

            _backendProcess.OutputDataReceived += (_, e) => { if (e.Data != null) AppLogger.Debug($"[Backend] {e.Data}"); };
            _backendProcess.ErrorDataReceived += (_, e) => { if (e.Data != null) AppLogger.Warn($"[Backend] {e.Data}"); };
            _backendProcess.BeginOutputReadLine();
            _backendProcess.BeginErrorReadLine();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Could not start STT backend: {ex.Message}");
            return false;
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
        try
        {
            if (_backendProcess is { HasExited: false })
            {
                _backendProcess.Kill(entireProcessTree: true);
                _backendProcess.WaitForExit(3000);
            }
        }
        catch { }
        _backendProcess?.Dispose();
        _sttBridgeClient.Dispose();
        _translationService.Dispose();
        _ttsController.Dispose();
    }
}
