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

/// <summary>一个侧边栏的独立配置。</summary>
public class SidebarConfig
{
    public string Name { get; set; } = "侧边栏";
    public DockEdge Edge { get; set; } = DockEdge.Left;
    public DockCorner Corner { get; set; } = DockCorner.None;
    public DockMode Mode { get; set; } = DockMode.Normal;

    /// <summary>沿边缘的偏移（DIPs）；-1 表示居中。</summary>
    public double EdgeOffset { get; set; } = -1;

    /// <summary>垂直于边缘的尺寸（L/R 边=宽度，T/B 边=高度）。</summary>
    public double PanelCross { get; set; } = 340;

    /// <summary>沿边缘的尺寸（L/R 边=高度，T/B 边=宽度）；0 表示自动（约工作区 55%）。</summary>
    public double PanelAlong { get; set; } = 0;

    public double Opacity { get; set; } = 0.92;
    public bool Acrylic { get; set; } = true;
    public string AccentColor { get; set; } = "#FF4C8DFF";
    public bool Pinned { get; set; }
    public bool EdgeTriggerFullSpan { get; set; }
    public bool FollowMouseMonitor { get; set; } = true;
    public int MonitorIndex { get; set; }
    public List<GroupModel> Groups { get; set; } = new();

    /// <summary>合并进来的子侧边栏（以页签形式显示在本侧边栏面板里）。</summary>
    public List<SidebarConfig> Tabs { get; set; } = new();
}
