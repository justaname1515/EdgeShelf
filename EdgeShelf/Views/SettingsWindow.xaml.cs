using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EdgeShelf.Models;
using EdgeShelf.Services;

namespace EdgeShelf.Views;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _main;
    private readonly List<(string Label, SidebarConfig Cfg)> _all = new();
    private string _barColor = "#FF4C8DFF";
    private string _panelColor = "#FF141A24";
    private bool _loading = true;
    private bool _syncing;
    private double _maxOffset = 500;
    private int _hkMods;
    private int _hkKey;
    private bool _hkCapturing;

    private static readonly string[] ColorPresets =
    {
        "#FF4C8DFF", "#FF7C4DFF", "#FF00BFA5", "#FF00C853",
        "#FFFF6D00", "#FFE91E63", "#FFFFB300", "#FFF44336",
        "#FF9E9E9E", "#FF000000", "#FFFFFFFF"
    };

    public SettingsWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;

        // 编辑目标：全部侧边栏（顶层 + 页签）
        foreach (var sb in ConfigService.Config.Sidebars)
        {
            _all.Add(($"{sb.Name}（侧边栏）", sb));
            int j = 1;
            foreach (var t in sb.Tabs)
                _all.Add(($"{sb.Name} ▸ {t.Name}（页签 {j}）", t));
        }
        SidebarCombo.ItemsSource = _all.Select(x => x.Label).ToList();
        ThemeSidebarCombo.ItemsSource = _all.Select(x => x.Label).ToList();

        // 默认选中当前窗口的侧边栏
        int selfIdx = _all.FindIndex(x => ReferenceEquals(x.Cfg, main.SidebarConfig));
        SidebarCombo.SelectedIndex = selfIdx >= 0 ? selfIdx : 0;
        ThemeSidebarCombo.SelectedIndex = selfIdx >= 0 ? selfIdx : 0;

        LoadGlobal();
        LoadSidebarTab();
        LoadThemeTab();

        _loading = false;
    }

    private SidebarConfig SidebarTarget => _all[Math.Max(0, SidebarCombo.SelectedIndex)].Cfg;
    private SidebarConfig ThemeTarget => _all[Math.Max(0, ThemeSidebarCombo.SelectedIndex)].Cfg;

    private static DockEdge EffectiveEdgeOf(SidebarConfig cfg)
        => cfg.Corner != DockCorner.None
            ? (cfg.Corner is DockCorner.TopLeft or DockCorner.BottomLeft ? DockEdge.Left : DockEdge.Right)
            : cfg.Edge;

    // ---------------- 加载 ----------------

    private void LoadGlobal()
    {
        var g = ConfigService.Config;
        AutoStartCheck.IsChecked = g.AutoStart;
        HotkeyEnableCheck.IsChecked = g.HotkeyEnabled;
        _hkMods = g.HotkeyModifiers;
        _hkKey = g.HotkeyKey;
        HotkeyBox.Text = FormatHotkey(_hkMods, _hkKey);
        CycleNormalCheck.IsChecked = g.CycleNormal;
        CycleTransparentCheck.IsChecked = g.CycleTransparent;
        CycleStealthCheck.IsChecked = g.CycleStealth;
        CyclePinnedCheck.IsChecked = g.CyclePinned;
    }

    private void LoadSidebarTab()
    {
        var cfg = SidebarTarget;
        var monitors = ScreenManager.Monitors;
        double workLenDip = 0;
        var m = monitors.Count > 0 ? monitors[Math.Clamp(cfg.MonitorIndex, 0, monitors.Count - 1)] : null;
        if (m != null)
        {
            bool vert = cfg.Corner != DockCorner.None
                ? cfg.Corner is DockCorner.TopLeft or DockCorner.TopRight or DockCorner.BottomLeft or DockCorner.BottomRight
                : cfg.Edge is DockEdge.Left or DockEdge.Right;
            workLenDip = vert ? m.WorkHeight / m.Scale : m.WorkWidth / m.Scale;
        }
        _maxOffset = Math.Max(0, workLenDip - 200);
        OffsetSlider.Maximum = _maxOffset;

        NameBox.Text = cfg.Name;
        EdgeCombo.SelectedIndex = cfg.Corner == DockCorner.None ? (int)cfg.Edge : (int)EffectiveEdgeOf(cfg);
        CornerCombo.SelectedIndex = (int)cfg.Corner;
        OffsetSlider.Value = cfg.EdgeOffset < 0 ? _maxOffset / 2 : Math.Clamp(cfg.EdgeOffset, 0, _maxOffset);
        CrossSlider.Value = Math.Clamp(cfg.PanelCross, CrossSlider.Minimum, CrossSlider.Maximum);
        AlongSlider.Value = Math.Clamp(cfg.PanelAlong > 0 ? cfg.PanelAlong : 400, AlongSlider.Minimum, AlongSlider.Maximum);
        OpacitySlider.Value = Math.Clamp(cfg.Opacity * 100, OpacitySlider.Minimum, OpacitySlider.Maximum);
        FullSpanCheck.IsChecked = cfg.EdgeTriggerFullSpan;
        FollowMouseCheck.IsChecked = cfg.FollowMouseMonitor;
        MonitorCombo.ItemsSource = monitors.Select(x => x.Label).ToList();
        MonitorCombo.SelectedIndex = Math.Clamp(cfg.MonitorIndex, 0, Math.Max(0, monitors.Count - 1));
        MonitorCombo.IsEnabled = cfg.FollowMouseMonitor;
        ModeNormalRadio.IsChecked = cfg.Mode == DockMode.Normal;
        ModeTransparentRadio.IsChecked = cfg.Mode == DockMode.Transparent;
        ModeStealthRadio.IsChecked = cfg.Mode == DockMode.Stealth;
        UpdateLabels(cfg);
    }

    private void LoadThemeTab()
    {
        var cfg = ThemeTarget;
        ThemeCombo.SelectedIndex = Math.Clamp((int)cfg.WindowTheme, 0, 5);
        DayNightCombo.SelectedIndex = cfg.DayNight == DayNight.Day ? 1 : 0;
        PanelTranslucentCheck.IsChecked = cfg.PanelTranslucent;
        _barColor = cfg.AccentColor;
        _panelColor = cfg.PanelColor;
        BuildSwatches(BarColorPanel, _barColor, BarColor_Click);
        BuildSwatches(PanelColorPanel, _panelColor, PanelColor_Click);
    }

    private void BuildSwatches(WrapPanel panel, string current, RoutedEventHandler click)
    {
        panel.Children.Clear();
        foreach (var preset in ColorPresets)
        {
            bool sel = string.Equals(preset, current, StringComparison.OrdinalIgnoreCase);
            var btn = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 4),
                Tag = preset,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset)),
                BorderThickness = new Thickness(sel ? 2 : 1),
                BorderBrush = sel ? Brushes.White : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = preset
            };
            if (sel)
            {
                var c = (Color)ColorConverter.ConvertFromString(preset);
                btn.Content = new TextBlock
                {
                    Text = "✓",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(IsLight(c) ? Colors.Black : Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            btn.Click += click;
            panel.Children.Add(btn);
        }
    }

    private static bool IsLight(Color c)
        => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150;

    private static string ToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private void UpdateLabels(SidebarConfig cfg)
    {
        bool vert = EffectiveEdgeOf(cfg) is DockEdge.Left or DockEdge.Right;
        CrossLabel.Text = vert ? "面板宽度" : "面板高度";
        AlongLabel.Text = vert ? "面板高度" : "面板宽度";
        AlongBox.Text = cfg.PanelAlong > 0 ? ((int)cfg.PanelAlong).ToString() : "自动";
    }

    // ---------------- 选择器与控件事件 ----------------

    private void SidebarCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && SidebarCombo.SelectedIndex >= 0) LoadSidebarTab();
    }

    private void ThemeSidebarCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeSidebarCombo.SelectedIndex >= 0) LoadThemeTab();
    }

    private void BarColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s)
        {
            _barColor = s;
            BuildSwatches(BarColorPanel, _barColor, BarColor_Click);
        }
    }

    private void PanelColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s)
        {
            _panelColor = s;
            BuildSwatches(PanelColorPanel, _panelColor, PanelColor_Click);
        }
    }

    /// <summary>主题下拉：选择主题时把默认配色填入色板（作为起点，之后仍可自行修改）。</summary>
    private void ThemeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo.SelectedIndex < 0) return;
        var theme = (WindowTheme)Math.Clamp(ThemeCombo.SelectedIndex, 0, 5);
        var d = MainWindow.ThemeDefaults(theme);
        if (d.Bar.HasValue) _barColor = ToHex(d.Bar.Value);
        if (d.Panel.HasValue) _panelColor = ToHex(d.Panel.Value);
        if (d.Day.HasValue) DayNightCombo.SelectedIndex = d.Day.Value ? 1 : 0;
        BuildSwatches(BarColorPanel, _barColor, BarColor_Click);
        BuildSwatches(PanelColorPanel, _panelColor, PanelColor_Click);
    }

    private void DayNightCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        // 白天/黑夜仅影响渲染，无需实时响应
    }

    /// <summary>恢复默认配色：无主题 / 默认蓝 / 夜晚 / 不透过（旧版本默认观感）。</summary>
    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var cfg = ThemeTarget;
        cfg.WindowTheme = WindowTheme.None;
        cfg.DayNight = DayNight.Night;
        cfg.PanelTranslucent = false;
        cfg.AccentColor = "#FF4C8DFF";
        cfg.PanelColor = "#FF141A24";
        LoadThemeTab(); // 重载界面，让色板/下拉反映默认值
    }

    private void EdgeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) UpdateLabels(SidebarTarget);
    }

    private void FollowMouse_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) MonitorCombo.IsEnabled = FollowMouseCheck.IsChecked != true;
    }

    private void ResetOffset_Click(object sender, RoutedEventArgs e)
    {
        SidebarTarget.EdgeOffset = -1;
        OffsetSlider.Value = _maxOffset / 2;
    }

    private void CrossSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || _loading || CrossBox == null) return;
        _syncing = true;
        CrossBox.Text = ((int)CrossSlider.Value).ToString();
        _syncing = false;
    }

    private void CrossBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _loading) return;
        if (double.TryParse(CrossBox.Text, out double v) && v >= CrossSlider.Minimum && v <= CrossSlider.Maximum)
        {
            _syncing = true;
            CrossSlider.Value = v;
            _syncing = false;
        }
    }

    private void AlongSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || _loading || AlongBox == null) return;
        _syncing = true;
        AlongBox.Text = ((int)AlongSlider.Value).ToString();
        _syncing = false;
    }

    private void AlongBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _loading) return;
        if (double.TryParse(AlongBox.Text, out double v) && v >= AlongSlider.Minimum && v <= AlongSlider.Maximum)
        {
            _syncing = true;
            AlongSlider.Value = v;
            _syncing = false;
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || _loading || OpacityBox == null) return;
        _syncing = true;
        OpacityBox.Text = ((int)OpacitySlider.Value).ToString();
        _syncing = false;
    }

    private void OpacityBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _loading) return;
        if (double.TryParse(OpacityBox.Text, out double v) && v >= OpacitySlider.Minimum && v <= OpacitySlider.Maximum)
        {
            _syncing = true;
            OpacitySlider.Value = v;
            _syncing = false;
        }
    }

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(ConfigService.DataDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ConfigService.DataDir) { UseShellExecute = true });
        }
        catch { }
    }

    private void NewSidebar_Click(object sender, RoutedEventArgs e)
    {
        _main.RequestNewSidebar();
        Close();
    }

    private void DeleteSidebar_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this, "确定删除当前页签？其分组配置会一并移除（磁盘上的文件夹不会动）。",
            "删除当前页签", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;
        _main.RequestDeleteCurrent();
        Close();
    }

    // ---------------- 全局快捷键捕获 ----------------

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _hkCapturing = true;
        HotkeyBox.Text = "按下组合键…（Esc 取消）";
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_hkCapturing) return;
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _hkCapturing = false;
            HotkeyBox.Text = FormatHotkey(_hkMods, _hkKey);
            return;
        }

        int mods = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= 2;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= 1;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= 4;
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) mods |= 8;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            HotkeyBox.Text = "继续按主键…";
            return;
        }
        if (mods == 0 || key == Key.None)
        {
            HotkeyBox.Text = "需要至少一个修饰键（Ctrl / Alt / Shift / Win）";
            return;
        }
        _hkMods = mods;
        _hkKey = KeyInterop.VirtualKeyFromKey(key);
        _hkCapturing = false;
        HotkeyBox.Text = FormatHotkey(_hkMods, _hkKey);
    }

    private static string FormatHotkey(int mods, int key)
    {
        if (key == 0) return "未设置";
        var parts = new List<string>();
        if ((mods & 2) != 0) parts.Add("Ctrl");
        if ((mods & 1) != 0) parts.Add("Alt");
        if ((mods & 4) != 0) parts.Add("Shift");
        if ((mods & 8) != 0) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey(key).ToString());
        return string.Join(" + ", parts);
    }

    /// <summary>标题栏 ✕ 关闭按钮。</summary>
    private void TitleClose_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>取消：不保存直接关闭（显式 Close，避免非模态窗口 IsCancel 不生效）。</summary>
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // ---- 整体配置 ----
        var g = ConfigService.Config;
        g.AutoStart = AutoStartCheck.IsChecked == true;
        ConfigService.SetAutoStart(g.AutoStart);
        g.HotkeyEnabled = HotkeyEnableCheck.IsChecked == true;
        g.HotkeyModifiers = _hkMods;
        g.HotkeyKey = _hkKey;
        g.CycleNormal = CycleNormalCheck.IsChecked == true;
        g.CycleTransparent = CycleTransparentCheck.IsChecked == true;
        g.CycleStealth = CycleStealthCheck.IsChecked == true;
        g.CyclePinned = CyclePinnedCheck.IsChecked == true;

        // ---- 侧边栏配置 ----
        var sb = SidebarTarget;
        sb.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "侧边栏" : NameBox.Text.Trim();
        if (CornerCombo.SelectedIndex == 0)
            sb.Edge = (DockEdge)EdgeCombo.SelectedIndex;
        sb.Corner = (DockCorner)CornerCombo.SelectedIndex;
        if (!(OffsetSlider.Value >= _maxOffset / 2 - 1 && OffsetSlider.Value <= _maxOffset / 2 + 1 && sb.EdgeOffset < 0))
        {
            sb.EdgeOffset = OffsetSlider.Value;
        }
        sb.PanelCross = Math.Round(CrossSlider.Value);
        sb.PanelAlong = Math.Round(AlongSlider.Value);
        sb.Opacity = Math.Round(OpacitySlider.Value) / 100.0;
        sb.EdgeTriggerFullSpan = FullSpanCheck.IsChecked == true;
        sb.FollowMouseMonitor = FollowMouseCheck.IsChecked == true;
        sb.MonitorIndex = Math.Max(0, MonitorCombo.SelectedIndex);
        sb.Mode = ModeTransparentRadio.IsChecked == true ? DockMode.Transparent
                : ModeStealthRadio.IsChecked == true ? DockMode.Stealth
                : DockMode.Normal;

        // ---- 主题配置 ----
        var th = ThemeTarget;
        th.WindowTheme = (WindowTheme)Math.Clamp(ThemeCombo.SelectedIndex, 0, 5);
        th.DayNight = DayNightCombo.SelectedIndex == 1 ? DayNight.Day : DayNight.Night;
        th.PanelTranslucent = PanelTranslucentCheck.IsChecked == true;
        th.AccentColor = _barColor;
        th.PanelColor = _panelColor;

        ConfigService.Save();

        // 应用到相关窗口（顶层侧边栏直接应用；页签由宿主在切换页签时应用）
        foreach (var w in MainWindow.Instances)
        {
            if (ReferenceEquals(w.SidebarConfig, sb) || ReferenceEquals(w.SidebarConfig, th) ||
                ReferenceEquals(w.ActiveConfig, sb) || ReferenceEquals(w.ActiveConfig, th) ||
                w.SidebarConfig.Tabs.Contains(sb) || w.SidebarConfig.Tabs.Contains(th))
            {
                w.ApplyConfig();
            }
        }

        _main.RefreshGroups();
        _main.Tray?.Refresh();
        _main.RequestHotkeyReapply();
        Close();
    }
}
