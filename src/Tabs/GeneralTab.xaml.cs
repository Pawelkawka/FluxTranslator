using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FluxTranslator.Core;

namespace FluxTranslator.Tabs;

public partial class GeneralTab : UserControl
{
    private AppConfig?      _config;
    private ConfigManager?  _manager;
    private AppController?  _controller;
    private bool            _loading;
    private DispatcherTimer? _downloadPollTimer;
    private HashSet<string> _installedModels = new(StringComparer.OrdinalIgnoreCase);

    public GeneralTab() => InitializeComponent();

    public void Initialise(AppConfig config, ConfigManager manager, AppController? controller = null)
    {
        _config     = config;
        _manager    = manager;
        _controller = controller;
        LoadValues();
    }

    public void Refresh() { if (_config is not null) LoadValues(); }

    private void LoadValues()
    {
        _loading = true;

        CbSourceLang.Items.Clear();
        int srcIdx = 0, i = 0;
        foreach (var (code, name) in AppSettings.SourceLanguages)
        {
            CbSourceLang.Items.Add(new ComboBoxItem { Content = name, Tag = code });
            if (code == _config!.SourceLanguage) srcIdx = i;
            i++;
        }
        CbSourceLang.SelectedIndex = srcIdx;

        CbTargetLang.Items.Clear();
        int tgtIdx = 0; i = 0;
        foreach (var (code, name) in AppSettings.TargetLanguages)
        {
            CbTargetLang.Items.Add(new ComboBoxItem { Content = name, Tag = code });
            if (code == _config!.TargetLanguage) tgtIdx = i;
            i++;
        }
        CbTargetLang.SelectedIndex = tgtIdx;

        TbUrl.Text = _config!.LibreTranslateUrl;
        RbLibreTranslate.IsChecked = _config!.TranslationEngine == TranslationEngine.LibreTranslate;
        RbCTranslate2.IsChecked    = _config!.TranslationEngine == TranslationEngine.CTranslate2;
        TbModelsDir.Text = ResolveModelsDir(_config!.CTranslate2ModelsDir);

        var srcPrefix = _config!.SourceLanguage.Split('-')[0].ToLowerInvariant();
        var tgtCode   = _config!.TargetLanguage.ToLowerInvariant();
        int dlSrcIdx = 0, dlTgtIdx = 0, j = 0;
        CbDownloadSrc.Items.Clear();
        CbDownloadTgt.Items.Clear();
        foreach (var (code, name) in AppSettings.CTranslate2Languages)
        {
            CbDownloadSrc.Items.Add(new ComboBoxItem { Content = name, Tag = code });
            CbDownloadTgt.Items.Add(new ComboBoxItem { Content = name, Tag = code });
            if (code == srcPrefix) dlSrcIdx = j;
            if (code == tgtCode)   dlTgtIdx = j;
            j++;
        }
        CbDownloadSrc.SelectedIndex = dlSrcIdx;
        CbDownloadTgt.SelectedIndex = dlTgtIdx;

        ApplyEngineVisibility(animate: false);
        ApplyLibreLanguageSelectionRules(persist: false);
        UpdateDownloadAvailability();

        _loading = false;
        if (_config!.TranslationEngine == TranslationEngine.CTranslate2)
            _ = RefreshModelsListAsync();
    }

    private void RbEngine_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || _config is null) return;
        _config.TranslationEngine = RbCTranslate2.IsChecked == true
            ? TranslationEngine.CTranslate2
            : TranslationEngine.LibreTranslate;
        _manager?.Save();
        ApplyEngineVisibility(animate: true);
        if (_config.TranslationEngine == TranslationEngine.CTranslate2)
            _ = RefreshModelsListAsync();
        else
            UpdateDownloadAvailability();
    }

    private void ApplyEngineVisibility(bool animate)
    {
        bool isCt2 = _config?.TranslationEngine == TranslationEngine.CTranslate2;
        bool showLibre = !isCt2;
        bool showCt2   = isCt2;

        SetVisibility(LanguagesSection, showLibre);
        SetVisibility(PanelLibreTranslate, showLibre);
        SetVisibility(PanelCTranslate2, showCt2);
        RowSourceLang.Visibility       = isCt2 ? Visibility.Collapsed : Visibility.Visible;
        RowTargetLang.Visibility       = isCt2 ? Visibility.Collapsed : Visibility.Visible;

        if (animate && IsLoaded)
        {
            if (showLibre)
            {
                AnimateFadeIn(LanguagesSection);
                AnimateFadeIn(PanelLibreTranslate);
            }
            else
            {
                AnimateFadeIn(PanelCTranslate2);
            }
        }
    }

    private static void SetVisibility(UIElement element, bool show)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        element.Opacity = show ? 1 : 0;
        element.IsHitTestVisible = show;
    }

    private static void AnimateFadeIn(UIElement element)
    {
        element.Opacity = 0;
        element.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
    }
}