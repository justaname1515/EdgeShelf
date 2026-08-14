using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using EdgeShelf.Models;
using Microsoft.Win32;

namespace EdgeShelf.Services;

public static class ConfigService
{
    public static AppConfig Config { get; private set; } = new();

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgeShelf");

    private static readonly string ConfigPath = Path.Combine(DataDir, "config.json");
    private static DispatcherTimer? _saveTimer;

    public static void Load()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null) Config = cfg;
            }
        }
        catch { }

        // 迁移：旧版单侧边栏配置 → Sidebars 列表
        if (Config.Sidebars.Count == 0)
        {
            Config.Sidebars.Add(new SidebarConfig
            {
                Name = "侧边栏 1",
                Edge = Config.Edge,
                Corner = DockCorner.None,
                EdgeOffset = -1,
                PanelCross = Config.PanelSize > 0 ? Config.PanelSize : 340,
                Opacity = Config.Opacity,
                Acrylic = Config.Acrylic,
                AccentColor = Config.AccentColor,
                Pinned = Config.Pinned,
                EdgeTriggerFullSpan = Config.EdgeTriggerFullSpan,
                FollowMouseMonitor = Config.FollowMouseMonitor,
                MonitorIndex = Config.MonitorIndex,
                Groups = Config.Groups ?? new()
            });
            SaveNow();
        }
        if (Config.Sidebars.Count == 0)
        {
            Config.Sidebars.Add(new SidebarConfig { Name = "侧边栏 1" });
        }

        Config.AutoStart = GetAutoStart();
    }

    /// <summary>延迟保存（合并频繁变更）。</summary>
    public static void Save()
    {
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public static void SaveNow()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static bool GetAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("EdgeShelf") != null;
        }
        catch { return false; }
    }

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                    exe = Path.Combine(AppContext.BaseDirectory, "EdgeShelf.exe");
                key?.SetValue("EdgeShelf", $"\"{exe}\"");
            }
            else
            {
                key?.DeleteValue("EdgeShelf", false);
            }
        }
        catch { }
    }
}
