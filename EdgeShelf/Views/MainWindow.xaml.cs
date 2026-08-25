using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using EdgeShelf.Models;
using EdgeShelf.Services;

namespace EdgeShelf.Views;

/// <summary>一个侧边栏窗口。每个侧边栏独立实例、独立配置。</summary>
public partial class MainWindow : Window
{
    private const double TabWidth = 10;        // 窄条厚度（DIPs）
    private const double PanelGap = 6;         // 窄条与面板间距
    private const double CornerArmLen = 90;    // L 形拐角横臂长度（DIPs）
    private const int EdgeThresholdPx = 5;     // 触发弹出的边缘距离（物理像素）
    private const int EdgeSpanMarginPx = 50;   // 窄条范围外的额外触发余量（物理像素）
    private const double MinCross = 280;       // 面板横向尺寸范围
    private const double MaxCross = 520;
    private const double MinAlong = 160;       // 面板沿边尺寸范围
    private const double MaxAlong = 900;

    private readonly SidebarConfig _cfg;

    private HwndSource? _hwndSource;
    private DispatcherTimer? _pollTimer;
    private DispatcherTimer? _closeTimer;
    private DispatcherTimer? _debounceTimer;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private SettingsWindow? _settingsWindow;

    private bool _open;
    private bool _suppressAutoHide;
    private bool _closing;
    private bool _resizingCross;
    private bool _resizingAlong;
    private bool _resizingAlong2;
    private double _resizeStart;
    private double _resizeStartSize;
    private double _resizeStartOffset2;
    private double _resizeStartAxis2;

    private bool _tabDragging;
    private bool _tabClickCandidate;
    private double _tabDragStartMouse;   // 物理像素（沿轴）
    private double _tabDragStartOffset;  // DIPs
    private MainWindow? _mergeTarget;    // 拖动蓝色栏时命中的合并目标

    private int _activeIndex;            // 当前选中的页签（0 = 本侧边栏自身）
    private DateTime _nearSince;
    private double _panelAlong = 400;    // 实际沿边尺寸（DIPs）
    private MonitorInfo _target = null!;

    public static readonly List<MainWindow> Instances = new();

    public TrayIcon? Tray { get; set; }
    public SidebarConfig SidebarConfig => _cfg;
    public event Action? NewSidebarRequested;
    public event Action? DeleteSidebarRequested;
    public event Action<MainWindow>? MergeRequested;
    public event Action<SidebarConfig>? UnmergeRequested;
    public event Action<SidebarConfig, MainWindow>? MergeTabRequested;
    public event Action? GlobalHotkeyPressed;
    public event Action? HotkeyReapplyRequested;

    public void RequestHotkeyReapply() => HotkeyReapplyRequested?.Invoke();

    /// <summary>当前选中的页签配置（决定显示哪一组抽屉）。</summary>
    public SidebarConfig ActiveConfig
    {
        get
        {
            if (_activeIndex > 0 && _cfg.Tabs.Count > 0)
                return _cfg.Tabs[Math.Min(_activeIndex - 1, _cfg.Tabs.Count - 1)];
            return _cfg;
        }
    }

    public MainWindow(SidebarConfig cfg)
    {
        _cfg = cfg;
        InitializeComponent();
        ApplyConfig();
        PinButton.IsChecked = _cfg.Pinned;
        GroupsView.AddGroupRequested += (_, _) => AddGroups();
        GroupsView.RenameRequested += (_, g) => RenameGroup(g);
        GroupsView.DeleteRequested += (_, g) => DeleteGroup(g);
        GroupsView.NewSubfolderRequested += (_, g) => CreateSubfolder(g);
        GroupsView.DeleteItemRequested += (it, g) => DeleteDrawerItem(it, g);
        GroupsView.MoveItemRequested += (it, from, to) => MoveDrawerItem(it, from, to);
        GroupsView.RenameItemRequested += (it, g) => RenameDrawerItem(it, g);
        GroupsView.MoveDrawerRequested += (g, move) => MoveDrawer(g, move);
        GroupsView.MoveGroupToSidebarRequested += (g, target) => MoveGroupToSidebar(g, target);
        GroupsView.DrawersProvider = src => AllDrawers().Where(g => !ReferenceEquals(g, src));
        GroupsView.SidebarsProvider = g => OtherSidebars(g);
        Instances.Add(this);
        RefreshTabs();

        // 蓝色栏右键：合并此侧边栏到另一个侧边栏
        Tab.ContextMenu = new ContextMenu();
        Tab.ContextMenuOpening += (_, _) => BuildTabBarMenu(Tab.ContextMenu!);
        if (TabH != null)
        {
            TabH.ContextMenu = Tab.ContextMenu;
        }
    }

    private void BuildTabBarMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        var mergeHeader = new MenuItem { Header = "合并此侧边栏到…" };
        var others = Instances.Where(w => w != this && !w._closing).ToList();
        if (others.Count == 0)
        {
            mergeHeader.Items.Add(new MenuItem { Header = "（没有其他侧边栏）", IsEnabled = false });
        }
        else
        {
            int i = 1;
            foreach (var other in others)
            {
                var target = other;
                var item = new MenuItem { Header = $"{i}. {other.SidebarConfig.Name}" };
                item.Click += (_, _) => MergeRequested?.Invoke(target);
                mergeHeader.Items.Add(item);
                i++;
            }
        }
        menu.Items.Add(mergeHeader);

        menu.Items.Add(new Separator());
        var del = new MenuItem { Header = "删除此侧边栏（含所有页签）" };
        del.Click += (_, _) => ConfirmDeleteSidebar();
        menu.Items.Add(del);
    }

    // ---------------- 几何 ----------------

    private DockEdge EffectiveEdge => IsCorner
        ? (_cfg.Corner is DockCorner.TopLeft or DockCorner.BottomLeft ? DockEdge.Left : DockEdge.Right)
        : _cfg.Edge;

    private bool IsCorner => _cfg.Corner != DockCorner.None;
    private bool IsVertical => EffectiveEdge is DockEdge.Left or DockEdge.Right;
    private double ClosedCrossDip => IsCorner ? TabWidth + CornerArmLen : TabWidth;
    private double OpenCrossDip => TabWidth + PanelGap + _cfg.PanelCross;
    private double WindowAlongDip => IsCorner ? TabWidth + PanelGap + _panelAlong : _panelAlong;

    private double ScreenX() { ScreenManager.GetCursorPos(out var p); return p.X; }
    private double ScreenY() { ScreenManager.GetCursorPos(out var p); return p.Y; }

    private void ComputePanelAlong(MonitorInfo m)
    {
        double workLenDip = IsVertical ? m.WorkHeight / m.Scale : m.WorkWidth / m.Scale;
        _panelAlong = _cfg.PanelAlong > 0
            ? Math.Clamp(_cfg.PanelAlong, MinAlong, Math.Max(MinAlong, workLenDip))
            : Math.Clamp(workLenDip * 0.55, MinAlong, 560);
    }

    private double EffectiveOffset(MonitorInfo m)
    {
        double workLenDip = IsVertical ? m.WorkHeight / m.Scale : m.WorkWidth / m.Scale;
        if (IsCorner)
        {
            bool atStart = _cfg.Corner is DockCorner.TopLeft or DockCorner.TopRight;
            return atStart ? 0 : Math.Max(0, workLenDip - WindowAlongDip);
        }
        if (_cfg.EdgeOffset < 0) return Math.Max(0, (workLenDip - _panelAlong) / 2);
        return Math.Clamp(_cfg.EdgeOffset, 0, Math.Max(0, workLenDip - _panelAlong));
    }

    private MonitorInfo TargetMonitor()
    {
        if (_cfg.FollowMouseMonitor)
        {
            ScreenManager.GetCursorPos(out var p);
            return ScreenManager.FromPoint(p.X, p.Y);
        }
        var monitors = ScreenManager.Monitors;
        return monitors[Math.Clamp(_cfg.MonitorIndex, 0, monitors.Count - 1)];
    }

    private void PlaceWindow()
    {
        var m = TargetMonitor();
        bool monitorChanged = _target == null || _target.Handle != m.Handle;
        _target = m;
        ComputePanelAlong(m);

        double s = m.Scale;
        double crossDip = _open ? OpenCrossDip : ClosedCrossDip;
        double alongDip = WindowAlongDip;
        double xDip, yDip;

        if (IsCorner)
        {
            bool left = EffectiveEdge == DockEdge.Left;
            bool top = _cfg.Corner is DockCorner.TopLeft or DockCorner.TopRight;
            xDip = left ? m.WorkArea.Left / s : m.WorkArea.Right / s - crossDip;
            yDip = top ? m.WorkArea.Top / s : m.WorkArea.Bottom / s - alongDip;
        }
        else if (IsVertical)
        {
            double startDip = m.WorkArea.Top / s + EffectiveOffset(m);
            yDip = startDip;
            xDip = EffectiveEdge == DockEdge.Left
                ? m.WorkArea.Left / s
                : m.WorkArea.Right / s - crossDip;
        }
        else
        {
            double startDip = m.WorkArea.Left / s + EffectiveOffset(m);
            xDip = startDip;
            yDip = EffectiveEdge == DockEdge.Top
                ? m.WorkArea.Top / s
                : m.WorkArea.Bottom / s - crossDip;
        }

        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);

        Left = xDip;
        Top = yDip;
        if (IsVertical)
        {
            Height = alongDip;
            Width = crossDip;
        }
        else
        {
            Width = alongDip;
            Height = crossDip;
        }
        ApplyEdgeLayout();

        if (monitorChanged)
        {
            Dispatcher.BeginInvoke(new Action(PlaceWindow), DispatcherPriority.Loaded);
        }
    }

    private void ApplyEdgeLayout()
    {
        if (IsCorner)
        {
            bool left = EffectiveEdge == DockEdge.Left;
            bool top = _cfg.Corner is DockCorner.TopLeft or DockCorner.TopRight;

            Tab.Width = TabWidth;
            Tab.Height = double.NaN;
            Tab.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            Tab.VerticalAlignment = VerticalAlignment.Stretch;

            TabH.Visibility = Visibility.Visible;
            TabH.Width = CornerArmLen;
            TabH.Height = TabWidth;
            TabH.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            TabH.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;

            PanelHost.Width = _cfg.PanelCross;
            PanelHost.Height = _panelAlong;
            PanelHost.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            PanelHost.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            PanelHost.Margin = new Thickness(
                left ? TabWidth + PanelGap : 0,
                top ? TabWidth + PanelGap : 0,
                left ? 0 : TabWidth + PanelGap,
                top ? 0 : TabWidth + PanelGap);
        }
        else if (IsVertical)
        {
            TabH.Visibility = Visibility.Collapsed;
            Tab.Width = TabWidth;
            Tab.Height = double.NaN;
            Tab.HorizontalAlignment = EffectiveEdge == DockEdge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            Tab.VerticalAlignment = VerticalAlignment.Stretch;

            PanelHost.Width = _cfg.PanelCross;
            PanelHost.Height = _panelAlong;
            PanelHost.HorizontalAlignment = EffectiveEdge == DockEdge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            PanelHost.VerticalAlignment = VerticalAlignment.Stretch;
            PanelHost.Margin = EffectiveEdge == DockEdge.Left
                ? new Thickness(TabWidth + PanelGap, 0, 0, 0)
                : new Thickness(0, 0, TabWidth + PanelGap, 0);
        }
        else
        {
            TabH.Visibility = Visibility.Collapsed;
            Tab.Width = double.NaN;
            Tab.Height = TabWidth;
            Tab.HorizontalAlignment = HorizontalAlignment.Stretch;
            Tab.VerticalAlignment = EffectiveEdge == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;

            PanelHost.Width = _panelAlong;
            PanelHost.Height = _cfg.PanelCross;
            PanelHost.HorizontalAlignment = HorizontalAlignment.Stretch;
            PanelHost.VerticalAlignment = EffectiveEdge == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            PanelHost.Margin = EffectiveEdge == DockEdge.Top
                ? new Thickness(0, TabWidth + PanelGap, 0, 0)
                : new Thickness(0, 0, 0, TabWidth + PanelGap);
        }

        // 箭头与圆角
        switch (EffectiveEdge)
        {
            case DockEdge.Left:
                TabArrow.Text = "\uE76C";
                Tab.CornerRadius = new CornerRadius(0, 8, 8, 0);
                Panel.CornerRadius = new CornerRadius(0, 12, 12, 0);
                break;
            case DockEdge.Right:
                TabArrow.Text = "\uE76B";
                Tab.CornerRadius = new CornerRadius(8, 0, 0, 8);
                Panel.CornerRadius = new CornerRadius(12, 0, 0, 12);
                break;
            case DockEdge.Top:
                TabArrow.Text = "\uE70D";
                Tab.CornerRadius = new CornerRadius(0, 0, 8, 8);
                Panel.CornerRadius = new CornerRadius(0, 0, 12, 12);
                break;
            default:
                TabArrow.Text = "\uE70E";
                Tab.CornerRadius = new CornerRadius(8, 8, 0, 0);
                Panel.CornerRadius = new CornerRadius(12, 12, 0, 0);
                break;
        }
        if (IsCorner)
        {
            bool top = _cfg.Corner is DockCorner.TopLeft or DockCorner.TopRight;
            TabH.CornerRadius = top
                ? new CornerRadius(8, 8, 0, 0)
                : new CornerRadius(0, 0, 8, 8);
        }

        // 宽度把手（十字向）
        ResizeGrip.Width = IsVertical ? 6 : double.NaN;
        ResizeGrip.Height = IsVertical ? double.NaN : 6;
        ResizeGrip.HorizontalAlignment = IsVertical
            ? (EffectiveEdge == DockEdge.Left ? HorizontalAlignment.Right : HorizontalAlignment.Left)
            : HorizontalAlignment.Stretch;
        ResizeGrip.VerticalAlignment = IsVertical
            ? VerticalAlignment.Stretch
            : (EffectiveEdge == DockEdge.Top ? VerticalAlignment.Bottom : VerticalAlignment.Top);
        ResizeGrip.Cursor = IsVertical ? Cursors.SizeWE : Cursors.SizeNS;

        // 高度/长度把手（沿边向，位于自由端：普通垂直边在底部，拐角下角在顶部）
        bool alongFarEndTop = IsCorner && _cfg.Corner is DockCorner.BottomLeft or DockCorner.BottomRight;
        ResizeGripAlong.Width = IsVertical ? double.NaN : 6;
        ResizeGripAlong.Height = IsVertical ? 6 : double.NaN;
        ResizeGripAlong.HorizontalAlignment = IsVertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        ResizeGripAlong.VerticalAlignment = IsVertical
            ? (alongFarEndTop ? VerticalAlignment.Top : VerticalAlignment.Bottom)
            : VerticalAlignment.Stretch;
        ResizeGripAlong.Cursor = IsVertical ? Cursors.SizeNS : Cursors.SizeWE;

        // 锚点端把手（拐角模式位置固定，不需要）
        if (IsCorner)
        {
            ResizeGripAlong2.Visibility = Visibility.Collapsed;
        }
        else
        {
            ResizeGripAlong2.Visibility = Visibility.Visible;
            ResizeGripAlong2.Width = ResizeGripAlong.Width;
            ResizeGripAlong2.Height = ResizeGripAlong.Height;
            ResizeGripAlong2.HorizontalAlignment = Flip(ResizeGripAlong.HorizontalAlignment);
            ResizeGripAlong2.VerticalAlignment = Flip(ResizeGripAlong.VerticalAlignment);
            ResizeGripAlong2.Cursor = ResizeGripAlong.Cursor;
        }
    }

    private static HorizontalAlignment Flip(HorizontalAlignment a)
        => a == HorizontalAlignment.Left ? HorizontalAlignment.Right
         : a == HorizontalAlignment.Right ? HorizontalAlignment.Left
         : HorizontalAlignment.Stretch;

    private static VerticalAlignment Flip(VerticalAlignment a)
        => a == VerticalAlignment.Top ? VerticalAlignment.Bottom
         : a == VerticalAlignment.Bottom ? VerticalAlignment.Top
         : VerticalAlignment.Stretch;

    // ---------------- 配置与应用 ----------------

    public void ApplyConfig()
    {
        // 固定 ⇄ 正常绑定：固定状态强制蓝条可见可触碰，避免"固定但碰不到"
        if (_cfg.Pinned) _cfg.Mode = DockMode.Normal;
        var vis = ActiveConfig; // 主题 / 配色 / 视图跟随当前页签
        ListViewToggle.IsChecked = vis.ListView;

        ApplyThemeResources(vis);

        // 模式：透明 / 无痕时窄条不可见
        // 注意：必须用 Brushes.Transparent（alpha=0 的透明画笔）显式清空该区域——
        // 用 null 画笔会让分层窗口表面留下未绘制的区域，渲染成黑块
        bool barVisible = _cfg.Mode == DockMode.Normal;
        var barBrush = (Brush)Application.Current.Resources["AccentBrush"];
        Tab.Background = barVisible ? barBrush : Brushes.Transparent;
        TabH.Background = barVisible ? barBrush : Brushes.Transparent;
        TabArrow.Visibility = barVisible ? Visibility.Visible : Visibility.Collapsed;

        PinButton.IsChecked = _cfg.Pinned;
        ApplyEdgeLayout();
        ReapplyEffect();
        PlaceWindow();

        // 从透明/无痕（隐藏态）切回正常：立即恢复显示窗口
        if (_cfg.Mode == DockMode.Normal && !IsVisible && _hwndSource != null)
        {
            Show();
            PlaceWindow();
        }
    }

    /// <summary>根据主题 / 白天黑夜 / 自定义配色刷新全局颜色资源。</summary>
    private void ApplyThemeResources(SidebarConfig vis)
    {
        var (bar, panel, day) = ThemeSheet.Resolve(vis);

        Application.Current.Resources["AccentBrush"] = new SolidColorBrush(bar);

        // 按键突起（全局）：勾选 = 云母风格凸起渐变；不勾选 = 简洁无按键
        if (ConfigService.Config.BtnRaised)
        {
            var ctl = ThemeSheet.Controls(WindowTheme.Mica);
            var grad = new LinearGradientBrush(ctl.BtnTop, ctl.BtnBottom, 90);
            grad.Freeze();
            Application.Current.Resources["BtnBrush"] = grad;
            Application.Current.Resources["BtnBorderBrush"] = new SolidColorBrush(ctl.BtnBorder);
            Application.Current.Resources["BtnTextBrush"] = new SolidColorBrush(ctl.BtnText);
        }
        else
        {
            Application.Current.Resources["BtnBrush"] = Brushes.Transparent;
            Application.Current.Resources["BtnBorderBrush"] = Brushes.Transparent;
            Application.Current.Resources["BtnTextBrush"] = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
        }

        // 面板透明度：不勾选透过 = 纯色不透明（255）；勾选透过 = 半透明，让模糊/桌面透出来
        byte alpha = 255;
        if (vis.PanelTranslucent)
            alpha = (byte)Math.Round(Math.Clamp(vis.Opacity, 0.0, 1.0) * 235 * 0.55);
        if (day) alpha = (byte)Math.Max((int)alpha, 230); // 浅色面板基本不透明，避免透出底色发灰
        Application.Current.Resources["PanelBrush"] = new SolidColorBrush(Color.FromArgb(alpha, panel.R, panel.G, panel.B));

        if (day)
        {
            Application.Current.Resources["TextBrush"] = new SolidColorBrush(Color.FromArgb(0xF2, 0x1A, 0x1A, 0x1A));
            Application.Current.Resources["SubTextBrush"] = new SolidColorBrush(Color.FromArgb(0xB0, 0x55, 0x55, 0x55));
            Application.Current.Resources["CardBrush"] = new SolidColorBrush(Color.FromArgb(0x16, 0x00, 0x00, 0x00));
            Application.Current.Resources["CardHoverBrush"] = new SolidColorBrush(Color.FromArgb(0x26, 0x00, 0x00, 0x00));
            Application.Current.Resources["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
            Application.Current.Resources["HeaderBrush"] = new SolidColorBrush(Color.FromArgb(0x1F, 0x00, 0x00, 0x00));
        }
        else
        {
            Application.Current.Resources["TextBrush"] = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
            Application.Current.Resources["SubTextBrush"] = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));
            Application.Current.Resources["CardBrush"] = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            Application.Current.Resources["CardHoverBrush"] = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
            Application.Current.Resources["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
            Application.Current.Resources["HeaderBrush"] = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x00, 0x00));
        }
    }

    private void ReapplyEffect()
    {
        // 分层窗口（AllowsTransparency）上应用云母 / 亚克力等 DWM 效果会闪烁、且底色会从缝隙透出棕色条，
        // 因此一律不使用系统效果——"透过"完全由 WPF 半透明实现
        if (_hwndSource == null) return;
        try { WindowEffects.Reset(_hwndSource.Handle); } catch { }
    }

    // ---------------- 开合动画 ----------------

    private void AnimateTo(bool open)
    {
        _open = open;

        // 透明 / 无痕：窗口平时隐藏（避免分层窗口黑块）。打开前先以当前（关闭态）尺寸显示，
        // 让滑出动画完整可见；收起动画结束后再隐藏，避免"突然消失/蹦出来"
        bool autoHidden = _cfg.Mode != DockMode.Normal;
        if (autoHidden && open && !IsVisible)
        {
            Show();
            BringToFront();
        }

        double crossDip = open ? OpenCrossDip : ClosedCrossDip;
        double dur = open ? 230 : 190;
        var ease = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };

        if (IsVertical)
        {
            BeginAnimation(WidthProperty, new DoubleAnimation(crossDip, TimeSpan.FromMilliseconds(dur)) { EasingFunction = ease });
            if (EffectiveEdge == DockEdge.Right && _target != null)
            {
                double endX = _target.WorkArea.Right / _target.Scale - crossDip;
                BeginAnimation(LeftProperty, new DoubleAnimation(endX, TimeSpan.FromMilliseconds(dur)) { EasingFunction = ease });
            }
        }
        else
        {
            BeginAnimation(HeightProperty, new DoubleAnimation(crossDip, TimeSpan.FromMilliseconds(dur)) { EasingFunction = ease });
            if (EffectiveEdge == DockEdge.Bottom && _target != null)
            {
                double endY = _target.WorkArea.Bottom / _target.Scale - crossDip;
                BeginAnimation(TopProperty, new DoubleAnimation(endY, TimeSpan.FromMilliseconds(dur)) { EasingFunction = ease });
            }
        }

        var fade = new DoubleAnimation(open ? 1 : 0, TimeSpan.FromMilliseconds(open ? 170 : 120));
        Panel.BeginAnimation(OpacityProperty, fade);

        if (!open && autoHidden && IsVisible)
        {
            // 等收起动画（几何 190ms + 淡出 120ms）全部结束再隐藏窗口
            var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(dur + 40) };
            hideTimer.Tick += (_, _) =>
            {
                hideTimer.Stop();
                if (!_open && _cfg.Mode != DockMode.Normal && IsVisible) Hide();
            };
            hideTimer.Start();
        }

        if (open) BringToFront();
    }

    private void BringToFront()
    {
        if (_hwndSource == null) return;
        if (_settingsWindow is { IsVisible: true }) return; // 设置窗口打开时别跳到它上面（防闪烁）
        SetWindowPos(_hwndSource.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // ---------------- 边缘触发 ----------------

    private void PollTick(object? sender, EventArgs e)
    {
        if (_closing) return;
        try
        {
            // 透明 / 无痕：窗口显隐由 AnimateTo 负责（保证动画完整），这里只做兜底
            bool autoHidden = _cfg.Mode != DockMode.Normal;
            if (autoHidden)
            {
                if (_open && !IsVisible) Show();
            }
            else if (!IsVisible)
            {
                Show(); // 从透明/无痕（隐藏态）切回正常：恢复显示
                PlaceWindow();
            }

            var m = TargetMonitor();
            bool monitorChanged = _target == null || _target.Handle != m.Handle;
            if (monitorChanged)
            {
                ScreenManager.GetCursorPos(out var probe);
                if (IsNearEdge(m, probe.X, probe.Y))
                    PlaceWindow();
                else if (_target != null)
                    m = _target;
            }
            else if (_target != null && Math.Abs(m.Scale - _target.Scale) > 0.001)
            {
                PlaceWindow();
            }

            ScreenManager.GetCursorPos(out var p);
            bool nearEdge = IsNearEdge(m, p.X, p.Y);
            bool overContent = IsOverContent(m, p.X, p.Y);

            bool wantOpen = _cfg.Pinned || _resizingCross || _resizingAlong || _tabDragging;
            if (_cfg.Mode != DockMode.Stealth)
            {
                // 透明 / 正常：边缘接近触发；无痕模式下完全关闭鼠标唤起
                wantOpen = wantOpen || overContent;
                if (nearEdge)
                {
                    if (_nearSince == default) _nearSince = DateTime.UtcNow;
                    else if ((DateTime.UtcNow - _nearSince).TotalMilliseconds > 140) wantOpen = true;
                }
                else
                {
                    _nearSince = default;
                }
            }
            else
            {
                _nearSince = default;
            }

            if (wantOpen)
            {
                _closeTimer?.Stop();
                if (!_open) AnimateTo(true);
            }
            else if (_open && !_suppressAutoHide && _closeTimer is { IsEnabled: false })
            {
                _closeTimer.Start();
            }
        }
        catch { }
    }

    private bool IsNearEdge(MonitorInfo m, int x, int y)
    {
        var wa = m.WorkArea;
        double s = m.Scale;
        if (IsCorner)
        {
            bool left = EffectiveEdge == DockEdge.Left;
            bool top = _cfg.Corner is DockCorner.TopLeft or DockCorner.TopRight;
            double armLenPx = CornerArmLen * s;
            double alongPx = WindowAlongDip * s;
            double cornerX = left ? wa.Left : wa.Right;
            double cornerY = top ? wa.Top : wa.Bottom;

            bool nearV = left ? x <= wa.Left + EdgeThresholdPx : x >= wa.Right - EdgeThresholdPx;
            bool inVSpan = Math.Abs(y - cornerY) <= alongPx + EdgeSpanMarginPx;
            bool nearH = top ? y <= wa.Top + EdgeThresholdPx : y >= wa.Bottom - EdgeThresholdPx;
            bool inHSpan = Math.Abs(x - cornerX) <= armLenPx + EdgeSpanMarginPx;
            return (nearV && inVSpan) || (nearH && inHSpan);
        }

        bool inSpan;
        switch (EffectiveEdge)
        {
            case DockEdge.Left:
                if (x > wa.Left + EdgeThresholdPx) return false;
                inSpan = InTabSpan(y, m, vertical: true);
                break;
            case DockEdge.Right:
                if (x < wa.Right - EdgeThresholdPx) return false;
                inSpan = InTabSpan(y, m, vertical: true);
                break;
            case DockEdge.Top:
                if (y > wa.Top + EdgeThresholdPx) return false;
                inSpan = InTabSpan(x, m, vertical: false);
                break;
            default:
                if (y < wa.Bottom - EdgeThresholdPx) return false;
                inSpan = InTabSpan(x, m, vertical: false);
                break;
        }
        return inSpan;
    }

    private bool InTabSpan(int coord, MonitorInfo m, bool vertical)
    {
        if (_cfg.EdgeTriggerFullSpan) return true;
        // 使用蓝色窄条的实际位置（可能被拖离中心）
        double s = m.Scale;
        double startPx = (vertical ? m.WorkArea.Top : m.WorkArea.Left) + EffectiveOffset(m) * s;
        double lenPx = _panelAlong * s;
        return coord >= startPx - EdgeSpanMarginPx && coord <= startPx + lenPx + EdgeSpanMarginPx;
    }

    private bool IsOverContent(MonitorInfo m, int x, int y)
    {
        var wa = m.WorkArea;
        double s = m.Scale;
        const int margin = 4;

        if (IsCorner)
        {
            bool left = EffectiveEdge == DockEdge.Left;
            bool top = _cfg.Corner is DockCorner.TopLeft or DockCorner.TopRight;
            double crossPx = (_open ? OpenCrossDip : ClosedCrossDip) * s;
            double alongPx = WindowAlongDip * s;
            double cornerX = left ? wa.Left : wa.Right;
            double cornerY = top ? wa.Top : wa.Bottom;
            double x0 = left ? cornerX - margin : cornerX - crossPx - margin;
            double x1 = left ? cornerX + crossPx + margin : cornerX + margin;
            double y0 = top ? cornerY - margin : cornerY - alongPx - margin;
            double y1 = top ? cornerY + alongPx + margin : cornerY + margin;
            return x >= x0 && x <= x1 && y >= y0 && y <= y1;
        }

        // 使用蓝色窄条的实际位置（可能被拖离中心）
        double offsetPx = EffectiveOffset(m) * s;
        double tabLenPx = _panelAlong * s;
        double tabTopPx = wa.Top + offsetPx;
        double tabLeftPx = wa.Left + offsetPx;
        double bandW = (_open ? OpenCrossDip : TabWidth) * s;

        switch (EffectiveEdge)
        {
            case DockEdge.Left:
                return x >= wa.Left - margin && x <= wa.Left + bandW + margin &&
                       y >= tabTopPx - margin && y <= tabTopPx + tabLenPx + margin;
            case DockEdge.Right:
                return x >= wa.Right - bandW - margin && x <= wa.Right + margin &&
                       y >= tabTopPx - margin && y <= tabTopPx + tabLenPx + margin;
            case DockEdge.Top:
                return y >= wa.Top - margin && y <= wa.Top + bandW + margin &&
                       x >= tabLeftPx - margin && x <= tabLeftPx + tabLenPx + margin;
            default:
                return y >= wa.Bottom - bandW - margin && y <= wa.Bottom + margin &&
                       x >= tabLeftPx - margin && x <= tabLeftPx + tabLenPx + margin;
        }
    }

    // ---------------- 窄条拖拽（移动位置 / 换边） ----------------

    private double CursorAlong() => IsVertical ? ScreenY() : ScreenX();

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsCorner)
        {
            // 拐角模式不支持拖动
            if (_open) ClosePanel(); else AnimateTo(true);
            return;
        }
        _tabDragging = true;
        _tabClickCandidate = true;
        _tabDragStartMouse = CursorAlong();
        _tabDragStartOffset = _target != null ? EffectiveOffset(_target) : 0;
        Tab.CaptureMouse();
        e.Handled = true;
    }

    private void Tab_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_tabDragging || _target == null) return;
        double cursorAlong = CursorAlong();
        if (Math.Abs(cursorAlong - _tabDragStartMouse) > 6) _tabClickCandidate = false;

        double s = _target.Scale;
        double workLenDip = IsVertical ? _target.WorkHeight / s : _target.WorkWidth / s;
        double newOffset = _tabDragStartOffset + (cursorAlong - _tabDragStartMouse) / s;
        _cfg.EdgeOffset = Math.Clamp(newOffset, 0, Math.Max(0, workLenDip - _panelAlong));
        PlaceWindow();
        CheckEdgeSwitch();
        if (!_tabClickCandidate) CheckMergeTarget();
        ConfigService.Save();
    }

    private void Tab_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_tabDragging) return;
        _tabDragging = false;
        Tab.ReleaseMouseCapture();
        if (_tabClickCandidate)
        {
            if (_open) ClosePanel(); else AnimateTo(true);
        }
        else if (_mergeTarget != null)
        {
            var target = _mergeTarget;
            _mergeTarget = null;
            MergeRequested?.Invoke(target);
        }
        _mergeTarget = null;
        ConfigService.Save();
    }

    /// <summary>拖动蓝色栏时，检测光标是否落在另一个侧边栏的蓝色栏上（命中即合并）。</summary>
    private void CheckMergeTarget()
    {
        ScreenManager.GetCursorPos(out var p);
        foreach (var other in Instances)
        {
            if (other == this || other._closing) continue;
            var r = other.GetTabScreenRect();
            if (r != Rect.Empty && r.Contains(new Point(p.X, p.Y)))
            {
                _mergeTarget = other;
                return;
            }
        }
        _mergeTarget = null;
    }

    /// <summary>本侧边栏蓝色栏的屏幕矩形（物理像素），用于合并命中检测。</summary>
    public Rect GetTabScreenRect()
    {
        if (_target == null) return Rect.Empty;
        var m = _target;
        double s = m.Scale;
        var wa = m.WorkArea;
        double offsetPx = EffectiveOffset(m) * s;
        double alongPx = WindowAlongDip * s;
        double thickPx = TabWidth * s;
        if (IsVertical)
        {
            double y0 = wa.Top + offsetPx;
            double x0 = EffectiveEdge == DockEdge.Left ? wa.Left : wa.Right - thickPx;
            return new Rect(x0, y0, thickPx, alongPx);
        }
        double xLeft = wa.Left + offsetPx;
        double yTop = EffectiveEdge == DockEdge.Top ? wa.Top : wa.Bottom - thickPx;
        return new Rect(xLeft, yTop, alongPx, thickPx);
    }

    // ---------------- 页签管理（合并进来的侧边栏） ----------------

    private void AddTab_Click(object sender, RoutedEventArgs e) => AddTab();

    /// <summary>在本侧边栏新建一个页签（侧边栏 N）。</summary>
    public void AddTab()
    {
        _cfg.Tabs.Add(new SidebarConfig
        {
            Name = NextSidebarName(),
            Edge = DockEdge.Left,
            Corner = DockCorner.None,
            EdgeOffset = -1,
            AccentColor = _cfg.AccentColor
        });
        SelectTab(_cfg.Tabs.Count);
        ConfigService.Save();
        Tray?.Refresh();
    }

    private static string NextSidebarName()
    {
        int total = ConfigService.Config.Sidebars.Count +
                    ConfigService.Config.Sidebars.Sum(s => s.Tabs.Count);
        return $"侧边栏 {total + 1}";
    }

    /// <summary>重建页签栏（自身 + 所有合并页签）。</summary>
    public void RefreshTabs()
    {
        TabStrip.Children.Clear();
        AddTabButton(_cfg, 0, isSelf: true);
        for (int i = 0; i < _cfg.Tabs.Count; i++)
            AddTabButton(_cfg.Tabs[i], i + 1, isSelf: false);
    }

    private void AddTabButton(SidebarConfig tab, int index, bool isSelf)
    {
        var btn = new ToggleButton
        {
            Style = (Style)FindResource("HeaderTextBtn"),
            Content = new TextBlock { Text = tab.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 110 },
            IsChecked = _activeIndex == index,
            DataContext = tab,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = isSelf
                ? "当前侧边栏（右键可合并/删除）"
                : "点击切换；右键可分离/合并/删除",
            ContextMenu = CreateTabMenu(tab, isSelf)
        };
        btn.Click += (_, _) => SelectTab(index);
        TabStrip.Children.Add(btn);
    }

    /// <summary>页签右键菜单：分离 / 合并到另一个侧边栏 / 删除。</summary>
    private ContextMenu CreateTabMenu(SidebarConfig tab, bool isSelf)
    {
        var menu = new ContextMenu();

        if (!isSelf)
        {
            var separate = new MenuItem { Header = "从此侧边栏分离（变成独立侧边栏）" };
            separate.Click += (_, _) => UnmergeRequested?.Invoke(tab);
            menu.Items.Add(separate);
            menu.Items.Add(new Separator());
        }

        var mergeHeader = new MenuItem { Header = isSelf ? "合并此侧边栏到…" : "合并此页签到…" };
        var others = Instances.Where(w => w != this && !w._closing).ToList();
        if (others.Count == 0)
        {
            mergeHeader.Items.Add(new MenuItem { Header = "（没有其他侧边栏）", IsEnabled = false });
        }
        else
        {
            int i = 1;
            foreach (var other in others)
            {
                var target = other;
                var item = new MenuItem { Header = $"{i}. {other.SidebarConfig.Name}" };
                item.Click += (_, _) =>
                {
                    if (isSelf) MergeRequested?.Invoke(target);
                    else MergeTabRequested?.Invoke(tab, target);
                };
                mergeHeader.Items.Add(item);
                i++;
            }
        }
        menu.Items.Add(mergeHeader);

        menu.Items.Add(new Separator());
        var rename = new MenuItem { Header = "重命名页签" };
        rename.Click += (_, _) => RenameTab(tab);
        menu.Items.Add(rename);

        menu.Items.Add(new Separator());
        if (isSelf)
        {
            var del = new MenuItem { Header = "删除此侧边栏（含所有页签）" };
            del.Click += (_, _) => ConfirmDeleteSidebar();
            menu.Items.Add(del);
        }
        else
        {
            var del = new MenuItem { Header = "删除此页签" };
            del.Click += (_, _) => ConfirmDeleteTab(tab);
            menu.Items.Add(del);
        }
        return menu;
    }

    private void ConfirmDeleteSidebar()
    {
        var r = MessageBox.Show(this, "确定删除此侧边栏（含其所有页签）？磁盘上的文件夹不会动。",
            "删除侧边栏", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r == MessageBoxResult.OK) DeleteSidebarRequested?.Invoke();
    }

    private void ConfirmDeleteTab(SidebarConfig tab)
    {
        var r = MessageBox.Show(this, "确定删除此页签？其分组配置会一并移除（磁盘上的文件夹不会动）。",
            "删除页签", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;
        if (_cfg.Tabs.Remove(tab))
        {
            SelectTab(Math.Min(_activeIndex, _cfg.Tabs.Count));
            ConfigService.Save();
            Tray?.Refresh();
        }
    }

    /// <summary>重命名页签（自身页签重命名侧边栏名）。</summary>
    private void RenameTab(SidebarConfig tab)
    {
        _suppressAutoHide = true;
        try
        {
            var dlg = new RenameDialog("重命名页签", "页签名称", tab.Name) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result))
            {
                tab.Name = dlg.Result.Trim();
                RefreshTabs();
                ConfigService.Save();
                Tray?.Refresh();
            }
        }
        finally { _suppressAutoHide = false; }
    }

    /// <summary>切换页签（0 = 自身）。</summary>
    public void SelectTab(int index)
    {
        _activeIndex = Math.Clamp(index, 0, _cfg.Tabs.Count);
        RefreshTabs();
        RefreshGroups();
        ListViewToggle.IsChecked = ActiveConfig.ListView; // 每个页签各自记忆视图模式
        ApplyConfig(); // 应用当前页签的主题 / 配色
    }

    public void SelectLastTab() => SelectTab(_cfg.Tabs.Count);

    /// <summary>拖动时检测是否靠近另一条边缘，若是则切换停靠边。</summary>
    private void CheckEdgeSwitch()
    {
        ScreenManager.GetCursorPos(out var p);
        var m = _target;
        double s = m.Scale;
        var wa = m.WorkArea;
        var best = (Edge: EffectiveEdge, Dist: int.MaxValue);
        void Consider(DockEdge edge, int dist)
        {
            if (dist < best.Dist) best = (edge, dist);
        }
        Consider(DockEdge.Left, Math.Abs(p.X - wa.Left));
        Consider(DockEdge.Right, Math.Abs(p.X - wa.Right));
        Consider(DockEdge.Top, Math.Abs(p.Y - wa.Top));
        Consider(DockEdge.Bottom, Math.Abs(p.Y - wa.Bottom));

        if (best.Dist < 60 && best.Edge != EffectiveEdge)
        {
            _cfg.Edge = best.Edge;
            _cfg.Corner = DockCorner.None;
            // 新边上的偏移：让窄条跟随光标
            double curAlong = best.Edge is DockEdge.Left or DockEdge.Right ? p.Y : p.X;
            double waStart = best.Edge is DockEdge.Left or DockEdge.Right ? wa.Top : wa.Left;
            double workLenDip = best.Edge is DockEdge.Left or DockEdge.Right ? m.WorkHeight / s : m.WorkWidth / s;
            _cfg.EdgeOffset = Math.Clamp((curAlong - waStart) / s - _panelAlong / 2, 0, Math.Max(0, workLenDip - _panelAlong));
            PlaceWindow();
        }
    }

    // ---------------- 手动调整大小 ----------------

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _resizingCross = true;
        _resizeStartSize = _cfg.PanelCross;
        _resizeStart = IsVertical ? ScreenX() : ScreenY();
        ResizeGrip.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizingCross || _target == null) return;
        double axis = IsVertical ? ScreenX() : ScreenY();
        double delta = (axis - _resizeStart) / _target.Scale;
        bool growsOut = IsVertical ? EffectiveEdge == DockEdge.Left : EffectiveEdge == DockEdge.Top;
        // 宽度上限 = 工作区横向尺寸（DIPs），不再写死 520
        double workCross = IsVertical ? _target.WorkWidth / _target.Scale : _target.WorkHeight / _target.Scale;
        double newSize = Math.Clamp(_resizeStartSize + (growsOut ? delta : -delta), MinCross, Math.Max(MinCross, workCross));
        if (Math.Abs(newSize - _cfg.PanelCross) > 0.5)
        {
            _cfg.PanelCross = Math.Round(newSize);
            PlaceWindow();
            ConfigService.Save();
        }
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizingCross)
        {
            _resizingCross = false;
            ResizeGrip.ReleaseMouseCapture();
            ConfigService.Save();
        }
    }

    private void ResizeGripAlong_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _resizingAlong = true;
        _resizeStartSize = _cfg.PanelAlong > 0 ? _cfg.PanelAlong : _panelAlong;
        _resizeStart = IsVertical ? ScreenY() : ScreenX();
        ResizeGripAlong.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGripAlong_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizingAlong || _target == null) return;
        double axis = IsVertical ? ScreenY() : ScreenX();
        double delta = (axis - _resizeStart) / _target.Scale;
        bool growsOut = IsCorner
            ? _cfg.Corner is DockCorner.TopLeft or DockCorner.TopRight
            : true;
        // 长度上限 = 工作区沿边尺寸（DIPs），不再写死 900
        double workLenDip = IsVertical ? _target.WorkHeight / _target.Scale : _target.WorkWidth / _target.Scale;
        double newSize = Math.Clamp(_resizeStartSize + (growsOut ? delta : -delta), MinAlong, Math.Max(MinAlong, workLenDip));
        if (Math.Abs(newSize - _panelAlong) > 0.5)
        {
            _cfg.PanelAlong = Math.Round(newSize);
            PlaceWindow();
            ConfigService.Save();
        }
    }

    private void ResizeGripAlong_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizingAlong)
        {
            _resizingAlong = false;
            ResizeGripAlong.ReleaseMouseCapture();
            ConfigService.Save();
        }
    }

    // 锚点端把手：拖动它时保持自由端不动（长度反向变化，位置跟随）
    private void ResizeGripAlong2_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsCorner) return;
        _resizingAlong2 = true;
        _resizeStartSize = _cfg.PanelAlong > 0 ? _cfg.PanelAlong : _panelAlong;
        _resizeStartOffset2 = _target != null ? EffectiveOffset(_target) : 0;
        ScreenManager.GetCursorPos(out var p);
        _resizeStartAxis2 = IsVertical ? p.Y : p.X;
        ResizeGripAlong2.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGripAlong2_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizingAlong2 || _target == null) return;
        ScreenManager.GetCursorPos(out var p);
        double axis = IsVertical ? p.Y : p.X;
        double delta = (axis - _resizeStartAxis2) / _target.Scale;
        double workLenDip = IsVertical ? _target.WorkHeight / _target.Scale : _target.WorkWidth / _target.Scale;
        double newLen = Math.Clamp(_resizeStartSize - delta, MinAlong, Math.Max(MinAlong, workLenDip));
        double newOffset = Math.Clamp(_resizeStartOffset2 + delta, 0, Math.Max(0, workLenDip - newLen));
        if (Math.Abs(newLen - _panelAlong) > 0.5 || Math.Abs(newOffset - EffectiveOffset(_target)) > 0.5)
        {
            _cfg.PanelAlong = Math.Round(newLen);
            _cfg.EdgeOffset = Math.Round(newOffset);
            PlaceWindow();
            ConfigService.Save();
        }
    }

    private void ResizeGripAlong2_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizingAlong2)
        {
            _resizingAlong2 = false;
            ResizeGripAlong2.ReleaseMouseCapture();
            ConfigService.Save();
        }
    }

    // ---------------- 分组管理 ----------------

    public void RefreshGroups()
    {
        foreach (var g in ActiveConfig.Groups) g.RefreshItems();
        GroupsView.SetGroups(ActiveConfig.Groups);
        GroupsView.SetViewMode(ActiveConfig.ListView);
        // 重新应用当前搜索词（切页签 / 增删抽屉后保持对当前侧边栏的过滤）
        var q = SearchBox.Text.Trim();
        if (q.Length > 0)
            foreach (var g in ActiveConfig.Groups) g.ApplySearch(q);
        SyncWatchers();
    }

    private void AddGroups()
    {
        _suppressAutoHide = true;
        try
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择要收纳的文件夹（可多选）",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) == true)
            {
                foreach (var p in dlg.FolderNames)
                {
                    if (ActiveConfig.Groups.Any(g => string.Equals(g.Path, p, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    ActiveConfig.Groups.Add(new GroupModel
                    {
                        Name = Path.GetFileName(p.TrimEnd('\\')),
                        Path = p
                    });
                }
                ConfigService.Save();
                RefreshGroups();
            }
        }
        finally { _suppressAutoHide = false; }
    }

    private void RenameGroup(GroupModel g)
    {
        _suppressAutoHide = true;
        try
        {
            var dlg = new RenameDialog("重命名分组", "分组名称", g.Name) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result))
            {
                g.Name = dlg.Result.Trim();
                ConfigService.Save();
                GroupsView.SetGroups(_cfg.Groups);
            }
        }
        finally { _suppressAutoHide = false; }
    }

    private void DeleteGroup(GroupModel g)
    {
        ActiveConfig.Groups.Remove(g);
        ConfigService.Save();
        RefreshGroups();
    }

    // ---------------- 抽屉内快捷方式：删除 / 移动 ----------------

    /// <summary>收集所有侧边栏（含合并页签）里的抽屉。</summary>
    private static IEnumerable<GroupModel> AllDrawers()
    {
        var list = new List<GroupModel>();
        void AddSidebar(SidebarConfig cfg)
        {
            list.AddRange(cfg.Groups);
            foreach (var t in cfg.Tabs) list.AddRange(t.Groups);
        }
        foreach (var w in Instances) AddSidebar(w.SidebarConfig);
        return list.Distinct();
    }

    /// <summary>收集所有侧边栏与合并页签（不含源抽屉所在的那个）。</summary>
    private static IEnumerable<SidebarConfig> OtherSidebars(GroupModel g)
    {
        var all = new List<SidebarConfig>();
        void AddSidebar(SidebarConfig cfg)
        {
            all.Add(cfg);
            foreach (var t in cfg.Tabs) all.Add(t);
        }
        foreach (var w in Instances) AddSidebar(w.SidebarConfig);
        var container = all.FirstOrDefault(sb => sb.Groups.Contains(g));
        return all.Where(sb => !ReferenceEquals(sb, container));
    }

    /// <summary>把抽屉整体移动到另一个侧边栏 / 页签（顺序持久化，目标窗口即时刷新）。</summary>
    private void MoveGroupToSidebar(GroupModel g, SidebarConfig target)
    {
        // 找到当前所在侧边栏 / 页签（本窗口的侧边栏或其合并页签）
        SidebarConfig? container = null;
        void FindIn(SidebarConfig sb)
        {
            if (container != null) return;
            if (sb.Groups.Contains(g)) container = sb;
            else foreach (var t in sb.Tabs) FindIn(t);
        }
        FindIn(_cfg);
        if (container == null || ReferenceEquals(container, target)) return;

        container.Groups.Remove(g);
        target.Groups.Add(g);
        ConfigService.Save();

        // 刷新本窗口与目标窗口的抽屉显示
        RefreshGroups();
        var tw = Instances.FirstOrDefault(w =>
            ReferenceEquals(w.SidebarConfig, target) || w.SidebarConfig.Tabs.Contains(target));
        if (tw != null && tw != this) tw.RefreshGroups();
    }

    /// <summary>删除抽屉里的快捷方式 / 文件（移入回收站）。</summary>
    private void DeleteDrawerItem(ItemInfo it, GroupModel g)
    {
        string confirmText = it.IsDirectory
            ? $"确定删除文件夹「{it.DisplayName}」？其内容将一并移入回收站。"
            : $"确定删除「{it.DisplayName}」？将移入回收站。";
        var confirm = MessageBox.Show(this, confirmText, "删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;
        try
        {
            if (it.IsDirectory)
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(it.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(it.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            g.RefreshItems();
            ConfigService.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"删除失败：{ex.Message}", "删除", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>把快捷方式 / 文件 / 文件夹移动到另一个抽屉（同名自动加序号）。</summary>
    private void MoveDrawerItem(ItemInfo it, GroupModel from, GroupModel to)
    {
        try
        {
            string dest = System.IO.Path.Combine(to.CurrentPath, it.Name);
            int i = 2;
            while (it.IsDirectory ? Directory.Exists(dest) : File.Exists(dest))
            {
                dest = System.IO.Path.Combine(to.CurrentPath,
                    $"{System.IO.Path.GetFileNameWithoutExtension(it.Name)} ({i}){System.IO.Path.GetExtension(it.Name)}");
                i++;
            }
            if (it.IsDirectory) Directory.Move(it.Path, dest);
            else File.Move(it.Path, dest);
            from.RefreshItems();
            to.RefreshItems();
            ConfigService.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"移动失败：{ex.Message}", "移动", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>头部按钮：切换 宫格 / 列表 视图（持久化到当前页签配置）。</summary>
    private void ListView_Click(object sender, RoutedEventArgs e)
    {
        ActiveConfig.ListView = ListViewToggle.IsChecked == true;
        GroupsView.SetViewMode(ActiveConfig.ListView);
        ConfigService.Save();
    }

    /// <summary>重命名抽屉里的文件 / 文件夹（磁盘上直接改名，快捷方式保留扩展名）。</summary>
    private void RenameDrawerItem(ItemInfo it, GroupModel g)
    {
        _suppressAutoHide = true;
        try
        {
            var ext = System.IO.Path.GetExtension(it.Path); // 文件夹为空
            string initial = ext.Length > 0 && it.Name.Length > ext.Length ? it.Name[..^ext.Length] : it.Name;
            var dlg = new RenameDialog("重命名", it.IsDirectory ? "文件夹名称" : "文件名称", initial) { Owner = this };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result)) return;

            string newName = dlg.Result.Trim();
            if (newName.Length == 0) return;
            if (ext.Length > 0 && !newName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) newName += ext;
            if (string.Equals(newName, it.Name, StringComparison.OrdinalIgnoreCase)) return;

            string newPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(it.Path) ?? "", newName);
            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                MessageBox.Show(this, $"已存在同名文件 / 文件夹：{newName}", "重命名失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                if (it.IsDirectory) Directory.Move(it.Path, newPath);
                else File.Move(it.Path, newPath);
            }
            catch
            {
                MessageBox.Show(this, "重命名失败：文件可能被占用或无权限。", "重命名失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // 同步手动排序里的旧名字
            for (int i = 0; i < g.OrderOverride.Count; i++)
                if (string.Equals(g.OrderOverride[i], it.Name, StringComparison.OrdinalIgnoreCase))
                    g.OrderOverride[i] = newName;
            g.RefreshItems();
            ConfigService.Save();
        }
        finally { _suppressAutoHide = false; }
    }

    /// <summary>抽屉右键菜单排序：上移 / 下移 / 置顶 / 置底（当前页签的抽屉顺序，随配置持久化）。</summary>
    private void MoveDrawer(GroupModel g, GroupsView.DrawerMove move)
    {
        var list = ActiveConfig.Groups;
        int i = list.IndexOf(g);
        if (i < 0) return;
        list.RemoveAt(i);
        int target = move switch
        {
            GroupsView.DrawerMove.Up => i - 1,
            GroupsView.DrawerMove.Down => i + 1,
            GroupsView.DrawerMove.Top => 0,
            GroupsView.DrawerMove.Bottom => list.Count,
            _ => i
        };
        target = Math.Clamp(target, 0, list.Count);
        list.Insert(target, g);
        RefreshGroups();
        ConfigService.Save();
    }

    private void CreateSubfolder(GroupModel g)
    {
        _suppressAutoHide = true;
        try
        {
            var dlg = new RenameDialog("新建子文件夹", "子文件夹名称", "新建文件夹") { Owner = this };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result)) return;
            string newPath = Path.Combine(g.CurrentPath, dlg.Result.Trim());
            if (Directory.Exists(newPath)) return;
            try
            {
                Directory.CreateDirectory(newPath);
                g.NavigateTo(newPath);
            }
            catch { }
        }
        finally { _suppressAutoHide = false; }
    }

    // ---------------- 快捷方式收集 ----------------

    private void AddShortcut_Click(object sender, RoutedEventArgs e) => AddShortcuts();

    private void AddShortcuts()
    {
        _suppressAutoHide = true;
        try
        {
            if (!EnsureShortcutFolder()) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择要收纳的快捷方式（可多选）",
                Multiselect = true,
                Filter = "快捷方式 (*.lnk;*.url)|*.lnk;*.url|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;

            int copied = CopyShortcuts(dlg.FileNames);
            if (copied > 0)
            {
                EnsureShortcutGroup();
                ConfigService.Save();
                RefreshGroups();
            }
        }
        finally { _suppressAutoHide = false; }
    }

    private bool EnsureShortcutFolder()
    {
        var cfg = ConfigService.Config;
        if (!string.IsNullOrEmpty(cfg.ShortcutFolder) && Directory.Exists(cfg.ShortcutFolder)) return true;

        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择快捷方式的存放文件夹（自己新建一个即可，卸载软件后快捷方式仍然保留）",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dlg.ShowDialog(this) != true) return false;
        cfg.ShortcutFolder = dlg.FolderName;
        ConfigService.Save();
        return true;
    }

    private int CopyShortcuts(IEnumerable<string> files)
        => CopyShortcutsTo(files, ConfigService.Config.ShortcutFolder);

    private static int CopyShortcutsTo(IEnumerable<string> files, string destFolder)
    {
        int n = 0;
        foreach (var f in files)
        {
            try
            {
                string name = Path.GetFileName(f);
                string dest = Path.Combine(destFolder, name);
                int i = 2;
                while (File.Exists(dest))
                {
                    dest = Path.Combine(destFolder,
                        $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
                    i++;
                }
                File.Copy(f, dest);
                n++;
            }
            catch { }
        }
        return n;
    }

    private void EnsureShortcutGroup()
    {
        var cfg = ConfigService.Config;
        if (string.IsNullOrEmpty(cfg.ShortcutFolder)) return;
        bool exists = ActiveConfig.Groups.Any(g => string.Equals(g.Path, cfg.ShortcutFolder, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            ActiveConfig.Groups.Add(new GroupModel
            {
                Name = Path.GetFileName(cfg.ShortcutFolder.TrimEnd('\\')),
                Path = cfg.ShortcutFolder
            });
        }
    }

    private void SyncWatchers()
    {
        var wanted = ActiveConfig.Groups
            .Where(g => Directory.Exists(g.Path))
            .Select(g => g.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in _watchers.ToList())
        {
            if (!wanted.Contains(kv.Key))
            {
                kv.Value.Dispose();
                _watchers.Remove(kv.Key);
            }
        }
        foreach (var p in wanted)
        {
            if (_watchers.ContainsKey(p)) continue;
            try
            {
                var w = new FileSystemWatcher(p) { IncludeSubdirectories = false, EnableRaisingEvents = true };
                w.Changed += OnGroupFolderChanged;
                w.Created += OnGroupFolderChanged;
                w.Deleted += OnGroupFolderChanged;
                w.Renamed += OnGroupFolderChanged;
                _watchers[p] = w;
            }
            catch { }
        }
    }

    private void OnGroupFolderChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_debounceTimer == null)
                {
                    _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                    _debounceTimer.Tick += (_, _) =>
                    {
                        _debounceTimer.Stop();
                        foreach (var g in ActiveConfig.Groups) g.RefreshItems();
                    };
                }
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }));
        }
        catch { }
    }

    // ---------------- 交互 ----------------

    public void SetPinned(bool pinned)
    {
        if (pinned) _cfg.Mode = DockMode.Normal; // 固定 ⇄ 正常绑定：固定时蓝条可见可触碰，不会"碰不到"
        _cfg.Pinned = pinned;
        ConfigService.Save();
        PinButton.IsChecked = pinned;
        Tray?.Refresh();
        if (pinned) ApplyConfig();
        if (pinned && !_open) AnimateTo(true);
    }

    /// <summary>切换模式（正常 / 透明 / 无痕）。</summary>
    public void SetMode(DockMode mode)
    {
        _cfg.Mode = mode;
        _cfg.Pinned = false; // 固定 ⇄ 正常绑定：任何模式切换都取消固定（否则固定→正常会卡死循环）
        if (mode != DockMode.Normal)
        {
            if (mode == DockMode.Stealth && _open) AnimateTo(false); // 无痕：自动隐藏，不再被鼠标唤起
            else if (!_open && IsVisible) Hide(); // 面板本就关闭：立即隐藏窗口，避免透明分层表面常驻（黑块风险）
        }
        ConfigService.Save();
        ApplyConfig();
        Tray?.Refresh();
    }

    /// <summary>进入无痕模式（界面按钮）。</summary>
    private void Stealth_Click(object sender, RoutedEventArgs e) => SetMode(DockMode.Stealth);

    /// <summary>全局快捷键：把本侧边栏推进到下一个已勾选模式（普→透→无→固 顺序）。
    /// 每次切换弹出模式提示气泡，避免透明 / 无痕等不可见状态让人以为快捷键失效。</summary>
    public void AdvanceMode()
    {
        var cfg = ConfigService.Config;
        var checkedModes = new List<int>();
        if (cfg.CycleNormal) checkedModes.Add(0);
        if (cfg.CycleTransparent) checkedModes.Add(1);
        if (cfg.CycleStealth) checkedModes.Add(2);
        if (cfg.CyclePinned) checkedModes.Add(3);
        if (checkedModes.Count == 0) return;

        int cur = _cfg.Pinned ? 3 : (int)_cfg.Mode;
        int idx = checkedModes.IndexOf(cur);
        int next = checkedModes[(idx + 1 + checkedModes.Count) % checkedModes.Count];
        switch (next)
        {
            case 0:
                SetMode(DockMode.Normal);
                ModeToast.Show("正常模式（普）");
                break;
            case 1:
                SetMode(DockMode.Transparent);
                ModeToast.Show("透明模式（透）");
                break;
            case 2:
                SetMode(DockMode.Stealth);
                ModeToast.Show("无痕模式（无）");
                break;
            case 3:
                SetPinned(true);
                ModeToast.Show("固定面板（固）");
                break;
        }
    }

    /// <summary>全局快捷键切换：固定 ⇄ 无痕（旧逻辑保留为兼容调用）。</summary>
    public void TogglePinnedStealth()
    {
        if (_cfg.Pinned) SetMode(DockMode.Stealth);
        else SetPinned(true);
    }

    private bool _hotkeyRegistered;

    public void RegisterHotkey(int modifiers, int key)
    {
        UnregisterHotkey();
        if (_hwndSource == null || key == 0) return;
        _hotkeyRegistered = RegisterHotKey(_hwndSource.Handle, HotkeyId, (uint)modifiers, (uint)key);
        try
        {
            File.AppendAllText(Path.Combine(ConfigService.DataDir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 热键注册 {(_hotkeyRegistered ? "成功" : "失败")} mods={modifiers} key={key}\r\n");
        }
        catch { }
    }

    public void UnregisterHotkey()
    {
        if (_hotkeyRegistered && _hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            _hotkeyRegistered = false;
        }
    }

    public void TogglePanel()
    {
        if (_open) ClosePanel();
        else AnimateTo(true);
    }

    private void ClosePanel()
    {
        if (_cfg.Pinned) SetPinned(false);
        AnimateTo(false);
    }

    public void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        _suppressAutoHide = true;
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _suppressAutoHide = false;
        };
        _settingsWindow.Show();
    }

    /// <summary>删除此侧边栏（App 负责收尾）。</summary>
    public void RequestDeleteSidebar() => DeleteSidebarRequested?.Invoke();

    /// <summary>删除当前页签（若删除自身且还有合并页签，则提升第一个页签为主体；无页签时删除整个侧边栏）。</summary>
    public void RequestDeleteCurrent() => DeleteActiveTab();

    private void DeleteActiveTab()
    {
        if (_cfg.Tabs.Count > 0)
        {
            if (_activeIndex == 0)
            {
                // 删除自身：把第一个页签提升为主体（保留窗口几何）
                var promoted = _cfg.Tabs[0];
                _cfg.Tabs.RemoveAt(0);
                _cfg.Name = promoted.Name;
                _cfg.Groups = promoted.Groups;
                _activeIndex = 0;
                RefreshTabs();
                RefreshGroups();
            }
            else
            {
                _cfg.Tabs.RemoveAt(_activeIndex - 1);
                SelectTab(Math.Min(_activeIndex - 1, _cfg.Tabs.Count));
            }
            ConfigService.Save();
            Tray?.Refresh();
        }
        else
        {
            DeleteSidebarRequested?.Invoke();
        }
    }

    public void RequestNewSidebar() => NewSidebarRequested?.Invoke();

    /// <summary>App 删除侧边栏前调用，允许窗口真正关闭。</summary>
    public void PrepareClose() => _closing = true;

    private void PinButton_Click(object sender, RoutedEventArgs e)
        => SetPinned(PinButton.IsChecked == true);

    private void AddGroup_Click(object sender, RoutedEventArgs e) => AddGroups();

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void ClosePanel_Click(object sender, RoutedEventArgs e) => ClosePanel();

    // ---------------- 搜索 ----------------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        SearchHint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var g in ActiveConfig.Groups) g.ApplySearch(q);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy;
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        _suppressAutoHide = true;
        try
        {
            int added = 0;
            var folders = new List<string>();
            var shortcuts = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p)) folders.Add(p);
                else if (p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                         p.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                    shortcuts.Add(p);
            }

            // 拖到某个抽屉卡片上：快捷方式直接复制进该抽屉的文件夹（不再弹选择文件夹框）
            if (shortcuts.Count > 0 && FindGroupAt(e.OriginalSource) is GroupModel target
                && Directory.Exists(target.CurrentPath))
            {
                added += CopyShortcutsTo(shortcuts, target.CurrentPath);
                target.RefreshItems();
            }
            else
            {
                foreach (var p in folders)
                {
                    if (ActiveConfig.Groups.Any(g => string.Equals(g.Path, p, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    ActiveConfig.Groups.Add(new GroupModel
                    {
                        Name = Path.GetFileName(p.TrimEnd('\\')),
                        Path = p
                    });
                    added++;
                }

                if (shortcuts.Count > 0 && EnsureShortcutFolder())
                {
                    added += CopyShortcuts(shortcuts);
                    EnsureShortcutGroup();
                }
            }

            if (added > 0)
            {
                ConfigService.Save();
                RefreshGroups();
            }
        }
        finally { _suppressAutoHide = false; }
    }

    /// <summary>从拖放事件的原始元素向上找到命中抽屉的 GroupModel。</summary>
    private static GroupModel? FindGroupAt(object? originalSource)
    {
        var el = originalSource as DependencyObject;
        while (el != null)
        {
            if (el is FrameworkElement fe && fe.DataContext is GroupModel g) return g;
            el = VisualTreeHelper.GetParent(el);
        }
        return null;
    }

    // ---------------- 窗口生命周期 ----------------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshGroups();
        PlaceWindow();
        if (_cfg.Pinned) AnimateTo(true);

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _pollTimer.Tick += PollTick;
        _pollTimer.Start();

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (_open && !_cfg.Pinned && !_suppressAutoHide) AnimateTo(false);
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
        _hwndSource.AddHook(WndProc);
        ReapplyEffect();
    }

    private void OnDpiChanged(object? sender, DpiChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(PlaceWindow));
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_closing)
        {
            e.Cancel = true;
            AnimateTo(false);
            return;
        }
        ConfigService.SaveNow();
    }

    protected override void OnClosed(EventArgs e)
    {
        Instances.Remove(this);
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DISPLAYCHANGE = 0x007E;
        if (msg == WM_DISPLAYCHANGE)
        {
            ScreenManager.Refresh();
            Dispatcher.BeginInvoke(new Action(PlaceWindow));
        }
        else if (msg == WM_NCHITTEST)
        {
            // 点击穿透：透明区域不拦截点击（修复转角框挡住窗口最小化/最大化/关闭按钮）
            int x = (short)((long)lParam & 0xFFFF);
            int y = (short)(((long)lParam >> 16) & 0xFFFF);
            if (!IsPointInContent(x, y))
            {
                handled = true;
                return new IntPtr(HTTRANSPARENT);
            }
            handled = true;
            return new IntPtr(HTCLIENT);
        }
        else if (msg == WM_HOTKEY)
        {
            try
            {
                File.AppendAllText(Path.Combine(ConfigService.DataDir, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 热键触发 (窗口 {_cfg.Name})\r\n");
            }
            catch { }
            GlobalHotkeyPressed?.Invoke();
            handled = true;
            return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    /// <summary>判断屏幕坐标是否落在可见内容上（用于点击穿透）。</summary>
    private bool IsPointInContent(int x, int y)
    {
        if (_target == null) return false;

        if (_cfg.Mode != DockMode.Normal)
        {
            // 透明 / 无痕：窄条不可触碰（鼠标掠过），只有展开的面板可点击
            // 注意：PointToScreen 已返回物理像素，不能再次乘以缩放系数
            var p = Panel.PointToScreen(new Point(0, 0));
            double px = p.X, py = p.Y;
            double pw = Math.Max(0, Panel.ActualWidth * _target.Scale);
            double ph = Math.Max(0, Panel.ActualHeight * _target.Scale);
            const int m = 2;
            return x >= px - m && x <= px + pw + m && y >= py - m && y <= py + ph + m;
        }

        if (_open) return IsOverContent(_target, x, y);

        if (IsCorner)
        {
            // 闭合转角：只算 L 形的条带区域
            var wa = _target.WorkArea;
            double s = _target.Scale;
            bool left = EffectiveEdge == DockEdge.Left;
            bool top = _cfg.Corner is DockCorner.TopLeft or DockCorner.TopRight;
            double cornerX = left ? wa.Left : wa.Right;
            double cornerY = top ? wa.Top : wa.Bottom;
            double vLen = WindowAlongDip * s;      // 垂直臂长度（窗口高）
            double hLen = CornerArmLen * s;          // 水平臂长度
            double T = TabWidth * s;
            const int m = 2;

            bool inV = left ? x >= wa.Left - m && x <= wa.Left + T + m
                            : x >= wa.Right - T - m && x <= wa.Right + m;
            bool inVSpan = top ? y >= cornerY - m && y <= cornerY + vLen + m
                               : y >= cornerY - vLen - m && y <= cornerY + m;
            bool inH = top ? y >= cornerY - T - m && y <= cornerY + m
                           : y >= cornerY - m && y <= cornerY + T + m;
            bool inHSpan = left ? x >= cornerX - hLen - m && x <= cornerX + m
                                : x >= cornerX - m && x <= cornerX + hLen + m;
            return (inV && inVSpan) || (inH && inHSpan);
        }

        return IsOverContent(_target, x, y);
    }

    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xE5F1;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTTRANSPARENT = -1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
}
