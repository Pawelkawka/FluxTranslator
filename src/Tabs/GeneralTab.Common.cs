using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluxTranslator.Core;

namespace FluxTranslator.Tabs;

public partial class GeneralTab
{
    private void ShowDownloadStatus(string progress, bool isError = false, string? errorDetail = null)
    {
        DownloadStatusPanel.Visibility = Visibility.Visible;
        TbDownloadProgress.Text = progress;

        if (isError && errorDetail is not null)
        {
            TbDownloadError.Text = errorDetail;
            TbDownloadError.Visibility = Visibility.Visible;
            return;
        }

        TbDownloadError.Visibility = Visibility.Collapsed;
    }

    private string GetSelectedDownloadModelName()
    {
        var src = (CbDownloadSrc.SelectedItem as ComboBoxItem)?.Tag as string;
        var tgt = (CbDownloadTgt.SelectedItem as ComboBoxItem)?.Tag as string;

        return (src is not null && tgt is not null)
            ? $"Helsinki-NLP/opus-mt-{src}-{tgt}"
            : string.Empty;
    }

    private static string NormalizeModelName(string modelName)
    {
        var clean = modelName.Trim().Replace('\\', '/');
        const string safePrefix = "Helsinki-NLP_";
        if (clean.StartsWith(safePrefix, StringComparison.OrdinalIgnoreCase))
            clean = "Helsinki-NLP/" + clean[safePrefix.Length..];
        return clean;
    }

    private static string FormatDownloadStatus(string message, string? modelName = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Waiting for download status...";

        var normalizedModel = string.IsNullOrWhiteSpace(modelName) ? null : NormalizeModelName(modelName);
        var cleanMessage = message.Trim();

        if (cleanMessage.StartsWith("Download started for ", StringComparison.OrdinalIgnoreCase))
            return $"Queued download\n{normalizedModel ?? cleanMessage["Download started for ".Length..].TrimEnd('.')}";

        if (cleanMessage.StartsWith("Downloading ", StringComparison.OrdinalIgnoreCase)
            && cleanMessage.Contains(" from HuggingFace", StringComparison.OrdinalIgnoreCase))
            return $"Downloading files from Hugging Face\n{normalizedModel ?? cleanMessage["Downloading ".Length..].Replace(" from HuggingFace…", string.Empty).Replace(" from HuggingFace...", string.Empty)}";

        if (cleanMessage.Equals("Converting to CTranslate2 format…", StringComparison.OrdinalIgnoreCase)
            || cleanMessage.Equals("Converting to CTranslate2 format...", StringComparison.OrdinalIgnoreCase))
            return $"Converting model to local CTranslate2 format\n{normalizedModel ?? string.Empty}".TrimEnd();

        if (cleanMessage.Equals("Copying tokenizer files…", StringComparison.OrdinalIgnoreCase)
            || cleanMessage.Equals("Copying tokenizer files...", StringComparison.OrdinalIgnoreCase))
            return $"Copying tokenizer files\n{normalizedModel ?? string.Empty}".TrimEnd();

        if (cleanMessage.Equals("Installation complete.", StringComparison.OrdinalIgnoreCase))
            return $"Installation complete\n{normalizedModel ?? string.Empty}".TrimEnd();

        return cleanMessage;
    }

    private static (string? src, string? tgt) ParseModelCodes(string modelName)
    {
        const string prefix = "opus-mt-";
        var clean = NormalizeModelName(modelName)
            .Replace("Helsinki-NLP/", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (!clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return (null, null);

        var rest = clean[prefix.Length..];
        var idx = rest.IndexOf('-');
        return idx < 0 ? (rest, null) : (rest[..idx], rest[(idx + 1)..]);
    }

    private static string ResolveModelsDir(string? path)
    {
        path ??= AppSettings.DefaultCTranslate2ModelsDir;
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
    }

    private static bool IsFromButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
