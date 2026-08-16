namespace EdgeShelf.Models;

public class AppConfig
{
    public List<SidebarConfig> Sidebars { get; set; } = new();

    /// <summary>用户自选/自建的快捷方式存放文件夹（卸载软件后快捷方式仍在）。</summary>
    public string ShortcutFolder { get; set; } = "";

    /// <summary>全局快捷键（按序循环切换已勾选的模式）。</summary>
    public bool HotkeyEnabled { get; set; }
    public int HotkeyModifiers { get; set; }
    public int HotkeyKey { get; set; }

    /// <summary>快捷键循环包含的模式（按 普→透→无→固 顺序循环勾选项）。</summary>
    public bool CycleNormal { get; set; } = true;
    public bool CycleTransparent { get; set; } = true;
    public bool CycleStealth { get; set; } = true;
    public bool CyclePinned { get; set; } = true;

    /// <summary>所有侧边栏共用同一套主题配色（全局主题）。</summary>
    public bool ThemeShareAll { get; set; }

    // ---- 以下为旧版单侧边栏字段，仅用于迁移 ----
    public DockEdge Edge { get; set; }
    public bool FollowMouseMonitor { get; set; } = true;
    public int MonitorIndex { get; set; }
    public double PanelSize { get; set; } = 340;
    public double Opacity { get; set; } = 0.92;
    public bool Acrylic { get; set; } = true;
    public string AccentColor { get; set; } = "#FF4C8DFF";
    public bool AutoStart { get; set; }
    public bool Pinned { get; set; }
    public bool EdgeTriggerFullSpan { get; set; }
    public List<GroupModel> Groups { get; set; } = new();
}
