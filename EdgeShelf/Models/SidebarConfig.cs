namespace EdgeShelf.Models;

public enum DockEdge
{
    Left,
    Right,
    Top,
    Bottom
}

/// <summary>L 形拐角模式：None 为普通边缘侧边栏。</summary>
public enum DockCorner
{
    None,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>侧边栏模式：控制窄条可见性、可触碰性与边缘触发。</summary>
public enum DockMode
{
    /// <summary>正常：蓝条可见、可拖动，边缘接近触发。</summary>
    Normal,
    /// <summary>透明：蓝条不可见且不可触碰（鼠标掠过），边缘接近仍触发。</summary>
    Transparent,
    /// <summary>无痕：蓝条不可见不可触碰，边缘触发关闭，只能托盘 / 快捷键恢复。</summary>
    Stealth
}

/// <summary>白天 / 黑夜：决定面板与文字的浅色 / 深色配色。</summary>
public enum DayNight
{
    /// <summary>黑夜（深色面板、浅色文字，默认）。</summary>
    Night,
    /// <summary>白天（浅色面板、深色文字）。</summary>
    Day
}

/// <summary>窗口主题：合成窗口效果 + 配色方案。</summary>
public enum WindowTheme
{
    /// <summary>无主题：使用自定义配色，无特殊窗口效果。</summary>
    None,
    /// <summary>云母（Win11 22H2+ 系统效果）。</summary>
    Mica,
    /// <summary>Aero：亚克力 / 毛玻璃模糊。</summary>
    Aero,
    /// <summary>WinXP Luna：经典 XP 蓝色调。</summary>
    Luna,
    /// <summary>Win98 Classic：经典银灰色调。</summary>
    Win98,
    /// <summary>Metro：扁平现代风格（强调色驱动）。</summary>
    Metro
}

/// <summary>一个侧边栏的独立配置。</summary>
public class SidebarConfig
{
    public string Name { get; set; } = "侧边栏";
    public DockEdge Edge { get; set; } = DockEdge.Left;
    public DockCorner Corner { get; set; } = DockCorner.None;
    public DockMode Mode { get; set; } = DockMode.Normal;

    /// <summary>内容视图：false=宫格（瓦片），true=列表（图标 + 名称竖排，文件夹内联展开）。</summary>
    public bool ListView { get; set; }

    /// <summary>沿边缘的偏移（DIPs）；-1 表示居中。</summary>
    public double EdgeOffset { get; set; } = -1;

    /// <summary>垂直于边缘的尺寸（L/R 边=宽度，T/B 边=高度）。</summary>
    public double PanelCross { get; set; } = 340;

    /// <summary>沿边缘的尺寸（L/R 边=高度，T/B 边=宽度）；0 表示自动（约工作区 55%）。</summary>
    public double PanelAlong { get; set; } = 0;

    public double Opacity { get; set; } = 0.92;

    /// <summary>窗口主题（无 / 云母 / Aero / Luna / Win98 / Metro）。</summary>
    public WindowTheme WindowTheme { get; set; } = WindowTheme.None;

    /// <summary>旧版亚克力开关（仅用于配置迁移：true → WindowTheme.Aero）。</summary>
    public bool Acrylic { get; set; }

    /// <summary>窄条主题色（蓝条与按钮高亮）。</summary>
    public string AccentColor { get; set; } = "#FF4C8DFF";

    /// <summary>面板主题色（面板背景基色）。</summary>
    public string PanelColor { get; set; } = "#FF141A24";

    /// <summary>白天 / 黑夜模式。</summary>
    public DayNight DayNight { get; set; } = DayNight.Night;

    /// <summary>面板是否透过（半透明 + 主题模糊效果；关闭则为纯色不透明面板）。</summary>
    public bool PanelTranslucent { get; set; }

    public bool Pinned { get; set; }
    public bool EdgeTriggerFullSpan { get; set; }
    public bool FollowMouseMonitor { get; set; } = true;
    public int MonitorIndex { get; set; }
    public List<GroupModel> Groups { get; set; } = new();

    /// <summary>合并进来的子侧边栏（以页签形式显示在本侧边栏面板里）。</summary>
    public List<SidebarConfig> Tabs { get; set; } = new();
}
