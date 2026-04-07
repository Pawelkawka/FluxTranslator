using System.Windows;
using System.Windows.Controls;
using System.Linq;
using FluxTranslator.Core;

namespace FluxTranslator.Tabs;

public partial class GeneralTab
{
    private void CbSourceLang_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config is null) return;
        ApplyLibreLanguageSelectionRules(persist: true);
    }

    private void CbTargetLang_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config is null) return;
        ApplyLibreLanguageSelectionRules(persist: true);
    }

    private void TbUrl_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _config is null) return;
        _config.LibreTranslateUrl = TbUrl.Text.Trim();
        _manager?.Save();
    }

    private void ApplyLibreLanguageSelectionRules(bool persist)
    {
        if (_config is null) return;

        var selectedSource = CbSourceLang.SelectedItem as ComboBoxItem;
        var selectedTarget = CbTargetLang.SelectedItem as ComboBoxItem;

        var sourcePrefix = selectedSource?.Tag is string sourceTag
            ? TranslationService.NormalizeLangCode(sourceTag)
            : null;
        var targetCode = selectedTarget?.Tag as string;

        foreach (var item in CbTargetLang.Items.OfType<ComboBoxItem>())
        {
            var code = item.Tag as string;
            item.IsEnabled = !string.Equals(code, sourcePrefix, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var item in CbSourceLang.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is not string srcTag)
            {
                item.IsEnabled = true;
                continue;
            }

            var srcCode = TranslationService.NormalizeLangCode(srcTag);
            item.IsEnabled = !string.Equals(srcCode, targetCode, StringComparison.OrdinalIgnoreCase);
        }

        if (sourcePrefix is not null
            && targetCode is not null
            && string.Equals(sourcePrefix, targetCode, StringComparison.OrdinalIgnoreCase))
        {
            var replacementTarget = CbTargetLang.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.IsEnabled);

            if (replacementTarget is not null)
            {
                CbTargetLang.SelectedItem = replacementTarget;
                targetCode = replacementTarget.Tag as string;
            }
        }

        if (!persist) return;

        if (selectedSource?.Tag is string finalSource)
            _config.SourceLanguage = finalSource;

        if (CbTargetLang.SelectedItem is ComboBoxItem finalTarget && finalTarget.Tag is string finalTargetCode)
            _config.TargetLanguage = finalTargetCode;

        _manager?.Save();
    }
}
