using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Linq;
using FluxTranslator.Core;

namespace FluxTranslator.Tabs;

public partial class GeneralTab
{
    private void CbDownloadLang_Changed(object sender, SelectionChangedEventArgs e)
        => UpdateDownloadAvailability();

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is null) return;

        var src = (CbDownloadSrc.SelectedItem as ComboBoxItem)?.Tag as string;
        var tgt = (CbDownloadTgt.SelectedItem as ComboBoxItem)?.Tag as string;

        if (src is null || tgt is null)
        {
            ShowDownloadStatus("Select source and target language first.", isError: true);
            return;
        }

        if (src == tgt)
        {
            ShowDownloadStatus("Source and target language must be different.", isError: true);
            return;
        }

        var modelName = GetSelectedDownloadModelName();
        BtnDownload.IsEnabled = false;
        ShowDownloadStatus($"Preparing download for {modelName}...");

        var (ok, msg) = await _controller.StartModelDownloadAsync(modelName);
        ShowDownloadStatus(FormatDownloadStatus(msg, modelName), isError: !ok);

        if (ok)
        {
            StartDownloadPolling(src, tgt);
            return;
        }

        if (msg.Contains("404") || msg.Contains("not found", StringComparison.OrdinalIgnoreCase))
            ShowDownloadStatus($"Model not found: {modelName}\nThis language pair may not have an opus-mt model available on Hugging Face.", isError: true);

        UpdateDownloadAvailability();
    }

    private void StartDownloadPolling(string srcCode, string tgtCode)
    {
        _downloadPollTimer?.Stop();
        _downloadPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _downloadPollTimer.Tick += async (_, _) => await PollDownloadStatusAsync(srcCode, tgtCode);
        _downloadPollTimer.Start();
        UpdateDownloadAvailability();
    }

    private async Task PollDownloadStatusAsync(string srcCode, string tgtCode)
    {
        if (_controller is null) return;

        var status = await _controller.GetModelDownloadStatusAsync();
        if (status is null) return;

        var progress = status.Progress ?? string.Empty;
        bool looksLike404 = progress.Contains("404")
            || progress.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(status.Error)
                && (status.Error.Contains("404") || status.Error.Contains("not found", StringComparison.OrdinalIgnoreCase)));

        ShowDownloadStatus(FormatDownloadStatus(progress, status.Model), isError: looksLike404);

        if (status.Success is null) return;

        _downloadPollTimer?.Stop();
        if (status.Success == true)
        {
            if (_config is not null)
            {
                _config.TargetLanguage = tgtCode;
                var match = AppSettings.SourceLanguages.FirstOrDefault(
                    kv => kv.Key.StartsWith(srcCode + "-", StringComparison.OrdinalIgnoreCase));
                if (match.Key is not null) _config.SourceLanguage = match.Key;
                _manager?.Save();
            }

            var model = $"Helsinki-NLP/opus-mt-{srcCode}-{tgtCode}";
            ShowDownloadStatus($"Installed and selected: {model}", isError: false);
            await RefreshModelsListAsync();
            return;
        }

        var errDetail = string.IsNullOrEmpty(status.Error) ? null : status.Error;
        bool isNotFound = (errDetail ?? progress).Contains("404")
            || (errDetail ?? progress).Contains("not found", StringComparison.OrdinalIgnoreCase);
        var failedModel = $"Helsinki-NLP/opus-mt-{srcCode}-{tgtCode}";

        ShowDownloadStatus(
            isNotFound
                ? $"Model not found: {failedModel}\nThis language pair may not have an opus-mt model on Hugging Face."
                : FormatDownloadStatus(progress, failedModel),
            isError: true,
            errorDetail: isNotFound ? null : errDetail);

        UpdateDownloadAvailability();
    }
}
