namespace EdgeShelf.Models;

public class AppConfig
{
    public List<SidebarConfig> Sidebars { get; set; } = new();

    /// <summary>用户自选/自建的快捷方式存放文件夹（卸载软件后快捷方式仍在）。</summary>
    public string ShortcutFolder { get; set; } = "";

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
