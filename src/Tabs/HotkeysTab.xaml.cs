using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluxTranslator.Core;

namespace FluxTranslator.Tabs;

public partial class HotkeysTab : UserControl
{
    private AppConfig     _config  = null!;
    private ConfigManager _manager = null!;

    private string _prevTrans = "";
    private string _prevCopy  = "";
    private string _prevKill  = "";

    public event Action? HotkeysChanged;

    public bool IsAnyHotkeyFocused =>
        TbTransHotkey.IsKeyboardFocused ||
        TbCopyHotkey.IsKeyboardFocused  ||
        TbKillHotkey.IsKeyboardFocused;

    public HotkeysTab()
    {
        InitializeComponent();
        PreviewMouseDown += HotkeysTab_PreviewMouseDown;
    }

    private void HotkeysTab_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!IsAnyHotkeyFocused) return;

        e.Handled = true;
        var focusedElement = Keyboard.FocusedElement;
        if (focusedElement is not TextBox focusedTextBox) return;

        string buttonName = e.ChangedButton switch
        {
            System.Windows.Input.MouseButton.Left => "LButton",
            System.Windows.Input.MouseButton.Right => "RButton",
            System.Windows.Input.MouseButton.Middle => "MButton",
            System.Windows.Input.MouseButton.XButton1 => "XButton1",
            System.Windows.Input.MouseButton.XButton2 => "XButton2",
            _ => null
        };

        if (buttonName is null) return;

        var mods = new List<string>();
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) mods.Add("Ctrl");
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods.Add("Shift");
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) mods.Add("Alt");

        mods.Add(buttonName);
        focusedTextBox.Text = string.Join("+", mods);

        if (focusedTextBox == TbTransHotkey)
            SaveTranslateHotkey(focusedTextBox.Text);
        else if (focusedTextBox == TbCopyHotkey)
            SaveCopyHotkey(focusedTextBox.Text);
        else if (focusedTextBox == TbKillHotkey)
            SaveKillHotkey(focusedTextBox.Text);
    }

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
        TbTransHotkey.Text = _config.HotkeyTranslate;
        TbCopyHotkey.Text  = _config.HotkeyCopy;
        TbKillHotkey.Text  = _config.HotkeyKillAll;
        LblStatus.Text     = string.Empty;
    }

    public void TbHotkey_GotFocus(object s, RoutedEventArgs e)
    {
        if (s is TextBox tb)
        {
            tb.SelectAll();
            if (tb == TbTransHotkey) _prevTrans = tb.Text;
            else if (tb == TbCopyHotkey) _prevCopy = tb.Text;
            else if (tb == TbKillHotkey) _prevKill = tb.Text;
        }
        LblStatus.Text = "Press a key combination or mouse button (e.g. Ctrl+M or LButton). Press ESC to cancel.";
    }

    public void TbHotkey_LostFocus(object s, RoutedEventArgs e)
    {
    }

    private void TbTransHotkey_KeyDown(object s, KeyEventArgs e)
    {
        if (IsEscape(e)) { CancelEdit(TbTransHotkey, _prevTrans, e); return; }
        if (CaptureHotkey(TbTransHotkey, e))
            SaveTranslateHotkey(TbTransHotkey.Text);
    }

    private void TbCopyHotkey_KeyDown(object s, KeyEventArgs e)
    {
        if (IsEscape(e)) { CancelEdit(TbCopyHotkey, _prevCopy, e); return; }
        if (CaptureHotkey(TbCopyHotkey, e))
            SaveCopyHotkey(TbCopyHotkey.Text);
    }

    private void TbKillHotkey_KeyDown(object s, KeyEventArgs e)
    {
        if (IsEscape(e)) { CancelEdit(TbKillHotkey, _prevKill, e); return; }
        if (CaptureHotkey(TbKillHotkey, e))
            SaveKillHotkey(TbKillHotkey.Text);
    }

    private static bool IsEscape(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return key == Key.Escape;
    }

    private void CancelEdit(TextBox tb, string original, KeyEventArgs e)
    {
        e.Handled  = true;
        tb.Text    = original;
        LblStatus.Text = "Cancelled.";
        Keyboard.ClearFocus();
    }

    private static bool CaptureHotkey(TextBox tb, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or
                   Key.LeftShift or Key.RightShift or
                   Key.LeftAlt or Key.RightAlt or
                   Key.LWin or Key.RWin or Key.Escape)
            return false;

        var mods = new List<string>();
        if (Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl))  mods.Add("Ctrl");
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods.Add("Shift");
        if (Keyboard.IsKeyDown(Key.LeftAlt)   || Keyboard.IsKeyDown(Key.RightAlt))   mods.Add("Alt");

        mods.Add(key.ToString());
        tb.Text = string.Join("+", mods);
        return true;
    }

    private bool HasConflict(string candidate, string skip)
    {
        var all = new[] { _config.HotkeyTranslate, _config.HotkeyCopy, _config.HotkeyKillAll };
        foreach (var h in all)
        {
            if (h.Equals(skip, StringComparison.OrdinalIgnoreCase)) continue;
            if (h.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private void SaveTranslateHotkey(string newHotkey)
    {
        newHotkey = newHotkey.Trim();
        if (string.IsNullOrEmpty(newHotkey)) return;

        if (HasConflict(newHotkey, _config.HotkeyTranslate))
        {
            LblStatus.Text     = "⚠ Conflict: this hotkey is already used by another action.";
            TbTransHotkey.Text = _prevTrans;
            return;
        }

        _config.HotkeyTranslate = newHotkey;
        _manager.Save();
        HotkeysChanged?.Invoke();
        LblStatus.Text = string.Empty;
        AppLogger.Info($"Translation hotkey updated: {newHotkey}");
        Keyboard.ClearFocus();
    }

    private void SaveCopyHotkey(string newHotkey)
    {
        newHotkey = newHotkey.Trim();
        if (string.IsNullOrEmpty(newHotkey)) return;

        if (HasConflict(newHotkey, _config.HotkeyCopy))
        {
            LblStatus.Text    = "⚠ Conflict: this hotkey is already used by another action.";
            TbCopyHotkey.Text = _prevCopy;
            return;
        }

        _config.HotkeyCopy = newHotkey;
        _manager.Save();
        HotkeysChanged?.Invoke();
        LblStatus.Text = string.Empty;
        AppLogger.Info($"Copy hotkey updated: {newHotkey}");
        Keyboard.ClearFocus();
    }

    private void SaveKillHotkey(string newHotkey)
    {
        newHotkey = newHotkey.Trim();
        if (string.IsNullOrEmpty(newHotkey)) return;

        if (HasConflict(newHotkey, _config.HotkeyKillAll))
        {
            LblStatus.Text    = "⚠ Conflict: this hotkey is already used by another action.";
            TbKillHotkey.Text = _prevKill;
            return;
        }

        _config.HotkeyKillAll = newHotkey;
        _manager.Save();
        HotkeysChanged?.Invoke();
        LblStatus.Text = string.Empty;
        AppLogger.Info($"Kill hotkey updated: {newHotkey}");
        Keyboard.ClearFocus();
    }
}
