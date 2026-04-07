using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using FluxTranslator.Core;

namespace FluxTranslator.Tabs;

public partial class GeneralTab
{
    private async Task RefreshModelsListAsync()
    {
        if (_controller is null) return;
        var models = await _controller.GetModelsAsync();
        Dispatcher.Invoke(() => PopulateModelsList(models));
    }

    private void PopulateModelsList(string[] models)
    {
        SpModelsList.Children.Clear();

        var distinctModels = models
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => new { Raw = m, Display = NormalizeModelName(m) })
            .GroupBy(m => m.Display, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(m => m.Display, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _installedModels = distinctModels
            .Select(m => NormalizeModelName(m.Raw))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (distinctModels.Length == 0)
        {
            NoModelsPanel.Visibility = Visibility.Visible;
            UpdateDownloadAvailability();
            return;
        }

        NoModelsPanel.Visibility = Visibility.Collapsed;

        var activeSource = TranslationService.NormalizeLangCode(_config?.SourceLanguage ?? string.Empty);
        var activeTarget = TranslationService.NormalizeLangCode(_config?.TargetLanguage ?? string.Empty);

        foreach (var model in distinctModels)
        {
            var (srcCode, tgtCode) = ParseModelCodes(model.Raw);
            bool isActive = srcCode is not null
                && tgtCode is not null
                && string.Equals(srcCode, activeSource, StringComparison.OrdinalIgnoreCase)
                && string.Equals(tgtCode, activeTarget, StringComparison.OrdinalIgnoreCase);

            SpModelsList.Children.Add(BuildModelRow(model.Raw, model.Display, srcCode, tgtCode, isActive));
        }

        UpdateDownloadAvailability();
    }

    private void BtnRefreshModels_Click(object sender, RoutedEventArgs e)
        => _ = RefreshModelsListAsync();

    private FrameworkElement BuildModelRow(string installedModelName, string displayModelName, string? srcCode, string? tgtCode, bool isActive)
    {
        var langPair = (srcCode is not null && tgtCode is not null)
            ? $"{srcCode.ToUpperInvariant()} → {tgtCode.ToUpperInvariant()}"
            : "MODEL";

        var outer = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 4),
        };

        outer.SetResourceReference(Border.BackgroundProperty, isActive ? "SurfaceBrush" : "CardBrush");
        outer.SetResourceReference(Border.BorderBrushProperty, isActive ? "AccentBrush" : "BorderBrush");
        outer.Cursor = isActive ? Cursors.Arrow : Cursors.Hand;
        outer.MouseLeftButtonUp += (_, e) =>
        {
            if (IsFromButton(e.OriginalSource as DependencyObject)) return;
            if (isActive) return;
            SelectInstalledModel(installedModelName);
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.SetResourceReference(Border.BackgroundProperty, isActive ? "AccentBrush" : "HoverBrush");
        badge.Child = new TextBlock
        {
            Text = langPair,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = isActive ? Brushes.White : (Brush)FindResource("TextPrimaryBrush"),
        };
        Grid.SetColumn(badge, 0);

        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameLabel = new TextBlock { Text = displayModelName, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        nameLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        nameStack.Children.Add(nameLabel);
        nameStack.ToolTip = installedModelName;
        Grid.SetColumn(nameStack, 1);

        var stateBadge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = isActive ? Visibility.Visible : Visibility.Collapsed,
        };
        stateBadge.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        stateBadge.Child = new TextBlock
        {
            Text = "Used",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        };
        Grid.SetColumn(stateBadge, 2);

        var delBtn = new Button
        {
            Content = "✕",
            Tag = installedModelName,
            Width = 28,
            Height = 28,
            ToolTip = "Delete model",
            VerticalAlignment = VerticalAlignment.Center,
        };
        delBtn.SetResourceReference(Button.StyleProperty, "DangerIconButton");
        delBtn.Click += BtnDeleteModel_Click;
        Grid.SetColumn(delBtn, 3);

        grid.Children.Add(badge);
        grid.Children.Add(nameStack);
        grid.Children.Add(stateBadge);
        grid.Children.Add(delBtn);
        outer.Child = grid;

        return outer;
    }

    private void SelectInstalledModel(string modelName)
    {
        if (_config is null) return;

        var (srcCode, tgtCode) = ParseModelCodes(modelName);
        if (srcCode is null || tgtCode is null) return;
        if (IsCurrentModelPair(srcCode, tgtCode)) return;

        _config.TargetLanguage = tgtCode;

        var match = AppSettings.SourceLanguages.FirstOrDefault(
            kv => kv.Key.StartsWith(srcCode + "-", StringComparison.OrdinalIgnoreCase));
        if (match.Key is not null)
            _config.SourceLanguage = match.Key;

        _manager?.Save();
        _ = RefreshModelsListAsync();
    }

    private bool IsCurrentModelPair(string srcCode, string tgtCode)
    {
        if (_config is null) return false;

        var activeSource = TranslationService.NormalizeLangCode(_config.SourceLanguage);
        var activeTarget = TranslationService.NormalizeLangCode(_config.TargetLanguage);

        return string.Equals(srcCode, activeSource, StringComparison.OrdinalIgnoreCase)
            && string.Equals(tgtCode, activeTarget, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateDownloadAvailability()
    {
        if (_config?.TranslationEngine != TranslationEngine.CTranslate2)
            return;

        if (_downloadPollTimer?.IsEnabled == true)
        {
            BtnDownload.IsEnabled = false;
            BtnDownload.Content = "Downloading...";
            return;
        }

        var modelName = GetSelectedDownloadModelName();
        if (string.IsNullOrWhiteSpace(modelName))
        {
            BtnDownload.IsEnabled = false;
            BtnDownload.Content = "Download Model";
            return;
        }

        bool alreadyInstalled = _installedModels.Contains(NormalizeModelName(modelName));
        if (alreadyInstalled)
        {
            BtnDownload.IsEnabled = false;
            BtnDownload.Content = "Model Already Installed";
            return;
        }

        BtnDownload.IsEnabled = true;
        BtnDownload.Content = "Download Model";
    }

    private async Task CommitModelsDirectoryChangeAsync()
    {
        if (_loading || _config is null)
            return;

        var sanitized = string.IsNullOrWhiteSpace(TbModelsDir.Text)
            ? AppSettings.DefaultCTranslate2ModelsDir
            : TbModelsDir.Text.Trim().Trim('"');

        bool changed = !string.Equals(_config.CTranslate2ModelsDir, sanitized, StringComparison.Ordinal);
        _config.CTranslate2ModelsDir = sanitized;

        var resolved = ResolveModelsDir(sanitized);
        if (!string.Equals(TbModelsDir.Text, resolved, StringComparison.Ordinal))
            TbModelsDir.Text = resolved;

        _manager?.Save();

        if (!changed)
            return;

        _installedModels.Clear();
        await RefreshModelsListAsync();
    }

    private async void TbModelsDir_LostFocus(object sender, RoutedEventArgs e)
        => await CommitModelsDirectoryChangeAsync();

    private async void TbModelsDir_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await CommitModelsDirectoryChangeAsync();
        Keyboard.ClearFocus();
    }

    private async void BtnBrowseModelsDir_Click(object sender, RoutedEventArgs e)
    {
        var currentDir = ResolveModelsDir(_config?.CTranslate2ModelsDir ?? AppSettings.DefaultCTranslate2ModelsDir);
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select the folder used to store offline translation models.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(currentDir)
                ? currentDir
                : AppDomain.CurrentDomain.BaseDirectory,
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;

        TbModelsDir.Text = dialog.SelectedPath;
        await CommitModelsDirectoryChangeAsync();
    }

    private async void BtnDeleteModel_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;

        var modelName = (sender as FrameworkElement)?.Tag as string;
        if (modelName is null) return;

        var dir = Path.Combine(ResolveModelsDir(_config.CTranslate2ModelsDir), modelName);
        if (!Directory.Exists(dir)) return;

        try
        {
            Directory.Delete(dir, recursive: true);
            await RefreshModelsListAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to delete model '{modelName}': {ex.Message}");
        }
    }

    private async void BtnOpenModelsDir_Click(object sender, RoutedEventArgs e)
    {
        await CommitModelsDirectoryChangeAsync();
        var dir = ResolveModelsDir(_config?.CTranslate2ModelsDir ?? AppSettings.DefaultCTranslate2ModelsDir);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }
}
