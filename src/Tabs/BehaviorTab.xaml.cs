using System.Windows.Controls;
using FluxTranslator.Core;

namespace FluxTranslator.Tabs;

public partial class BehaviorTab : UserControl
{
    private AppConfig     _config  = null!;
    private ConfigManager _manager = null!;
    private bool          _loading;

    public BehaviorTab() => InitializeComponent();

    public void Initialise(AppConfig config, ConfigManager manager)
    {
        _config  = config;
        _manager = manager;
        LoadValues();
    }

    public void Refresh()
    {
        if (_config is null) return;
        LoadValues();
    }

    private void LoadValues()
    {
        _loading = true;

        SlDisplayTime.Value = Math.Clamp(_config.OverlayDisplayTime, SlDisplayTime.Minimum, SlDisplayTime.Maximum);
        LblDisplayTime.Text = $"{SlDisplayTime.Value}s";

        _loading = false;
    }

    private void SlDisplayTime_ValueChanged(object s, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (LblDisplayTime != null)
            LblDisplayTime.Text = $"{e.NewValue}s";

        if (_loading || _config is null) return;
        _config.OverlayDisplayTime = (int)SlDisplayTime.Value;
        _manager.Save();
    }


}
