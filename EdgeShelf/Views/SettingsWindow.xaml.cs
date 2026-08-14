using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EdgeShelf.Models;
using EdgeShelf.Services;

namespace EdgeShelf.Views;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _main;
    private readonly SidebarConfig _cfg;
    private string _accent = "#FF4C8DFF";
    private bool _loading = true;
    private bool _syncing;
    private double _maxOffset = 500;

    private static readonly string[] AccentPresets =
    {
        "#FF4C8DFF", "#FF7C4DFF", "#FF00BFA5", "#FF00C853",
        "#FFFF6D00", "#FFE91E63", "#FFFFB300", "#FFF44336"
    };

    public SettingsWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        _cfg = main.SidebarConfig;

        var monitors = ScreenManager.Monitors;
        double workLenDip = 0;
        var m = monitors.Count > 0 ? monitors[Math.Clamp(_cfg.MonitorIndex, 0, monitors.Count - 1)] : null;
        if (m != null)
        {
            bool vert = _cfg.Corner != DockCorner.None
                ? _cfg.Corner is DockCorner.TopLeft or DockCorner.TopRight or DockCorner.BottomLeft or DockCorner.BottomRight
                : _cfg.Edge is DockEdge.Left or DockEdge.Right;
            workLenDip = vert ? m.WorkHeight / m.Scale : m.WorkWidth / m.Scale;
        }

        NameBox.Text = _cfg.Name;
        EdgeCombo.SelectedIndex = _cfg.Corner == DockCorner.None ? (int)_cfg.Edge : (int)EffectiveEdgeOf(_cfg);
        CornerCombo.SelectedIndex = (int)_cfg.Corner;

        _maxOffset = Math.Max(0, workLenDip - 200);
        OffsetSlider.Maximum = _maxOffset;
        double offset = _cfg.EdgeOffset < 0 ? _maxOffset / 2 : Math.Clamp(_cfg.EdgeOffset, 0, _maxOffset);
        OffsetSlider.Value = offset;

        CrossSlider.Value = Math.Clamp(_cfg.PanelCross, CrossSlider.Minimum, CrossSlider.Maximum);
        AlongSlider.Value = Math.Clamp(_cfg.PanelAlong > 0 ? _cfg.PanelAlong : 400, AlongSlider.Minimum, AlongSlider.Maximum);
        OpacitySlider.Value = Math.Clamp(_cfg.Opacity * 100, OpacitySlider.Minimum, OpacitySlider.Maximum);

        AcrylicCheck.IsChecked = _cfg.Acrylic;
        FullSpanCheck.IsChecked = _cfg.EdgeTriggerFullSpan;
        FollowMouseCheck.IsChecked = _cfg.FollowMouseMonitor;
        MonitorCombo.ItemsSource = monitors.Select(x => x.Label).ToList();
        MonitorCombo.SelectedIndex = Math.Clamp(_cfg.MonitorIndex, 0, Math.Max(0, monitors.Count - 1));
        MonitorCombo.IsEnabled = _cfg.FollowMouseMonitor;
        AutoStartCheck.IsChecked = ConfigService.Config.AutoStart;
        _accent = _cfg.AccentColor;

        foreach (var preset in AccentPresets)
        {
            var btn = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                Tag = preset,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = preset
            };
            btn.Click += Accent_Click;
            AccentPanel.Children.Add(btn);
        }

        // 透明配色：窄条隐形（边缘悬停/点击仍然可用）
        var transparentBtn = new Button
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = "#00000000",
            Content = new TextBlock
            {
                Text = "透",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "透明（窄条隐形，鼠标移到边缘仍可弹出）"
        };
        transparentBtn.Click += Accent_Click;
        AccentPanel.Children.Add(transparentBtn);

        UpdateLabels();
        _loading = false;
    }

    private static DockEdge EffectiveEdgeOf(SidebarConfig cfg)
        => cfg.Corner != DockCorner.None
            ? (cfg.Corner is DockCorner.TopLeft or DockCorner.BottomLeft ? DockEdge.Left : DockEdge.Right)
            : cfg.Edge;

    private bool IsVertical => EffectiveEdgeOf(_cfg) is DockEdge.Left or DockEdge.Right;

    private void UpdateLabels()
    {
        bool vert = IsVertical;
        CrossLabel.Text = vert ? "面板宽度" : "面板高度";
        AlongLabel.Text = vert ? "面板高度" : "面板宽度";
        AlongBox.Text = _cfg.PanelAlong > 0 ? ((int)_cfg.PanelAlong).ToString() : "自动";
    }

    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s) _accent = s;
    }

    private void EdgeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) UpdateLabels();
    }

    private void FollowMouse_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) MonitorCombo.IsEnabled = FollowMouseCheck.IsChecked != true;
    }

    private void OffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // 实时预览位置
        if (_loading || _cfg == null || _main == null) return;
        if (_cfg.EdgeOffset != OffsetSlider.Value)
        {
            _cfg.EdgeOffset = OffsetSlider.Value;
            _main.ApplyConfig();
        }
    }

    private void ResetOffset_Click(object sender, RoutedEventArgs e)
    {
        _cfg.EdgeOffset = -1;
        OffsetSlider.Value = _maxOffset / 2;
        _main.ApplyConfig();
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _cfg.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "侧边栏" : NameBox.Text.Trim();
        if (CornerCombo.SelectedIndex == 0)
            _cfg.Edge = (DockEdge)EdgeCombo.SelectedIndex;
        _cfg.Corner = (DockCorner)CornerCombo.SelectedIndex;
        if (OffsetSlider.Value >= _maxOffset / 2 - 1 && OffsetSlider.Value <= _maxOffset / 2 + 1 && _cfg.EdgeOffset < 0)
        {
            // 保持居中
        }
        else
        {
            _cfg.EdgeOffset = OffsetSlider.Value;
        }
        _cfg.PanelCross = Math.Round(CrossSlider.Value);
        _cfg.PanelAlong = Math.Round(AlongSlider.Value);
        _cfg.Opacity = Math.Round(OpacitySlider.Value) / 100.0;
        _cfg.Acrylic = AcrylicCheck.IsChecked == true;
        _cfg.EdgeTriggerFullSpan = FullSpanCheck.IsChecked == true;
        _cfg.FollowMouseMonitor = FollowMouseCheck.IsChecked == true;
        _cfg.MonitorIndex = Math.Max(0, MonitorCombo.SelectedIndex);
        _cfg.AccentColor = _accent;
        ConfigService.Config.AutoStart = AutoStartCheck.IsChecked == true;
        ConfigService.SetAutoStart(ConfigService.Config.AutoStart);
        ConfigService.Save();

        _main.ApplyConfig();
        _main.RefreshGroups();
        Close();
    }
}
