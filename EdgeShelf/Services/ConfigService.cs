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
                if (!TryLoad(ConfigPath))
                {
                    // 主配置损坏 / 读取失败：尝试从上次成功保存的备份恢复
                    string bak = Path.Combine(DataDir, "config.json.bak");
                    if (TryLoad(bak))
                    {
                        Log($"配置损坏，已从备份恢复：{ConfigPath}");
                        try { File.Copy(bak, ConfigPath, true); } catch { }
                    }
                    else
                    {
                        Log($"配置损坏且无有效备份，已重置为默认（原文件保留为 config.json.corrupt）：{ConfigPath}");
                        try { File.Copy(ConfigPath, Path.Combine(DataDir, "config.json.corrupt"), true); } catch { }
                    }
                }
                else
                {
                    // 成功加载：保留一份"上次成功"的备份，供下次损坏时恢复
                    try { File.Copy(ConfigPath, Path.Combine(DataDir, "config.json.bak"), true); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"读取配置失败：{ex}");
        }

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

        // 迁移：旧版“透”主题色（#00000000）→ 透明模式（主题色恢复默认），透明改由“模式”表达
        bool accentMigrated = false;
        foreach (var sb in Config.Sidebars)
        {
            if (sb.AccentColor == "#00000000")
            {
                sb.AccentColor = "#FF4C8DFF";
                sb.Mode = DockMode.Transparent;
                accentMigrated = true;
            }
            // 旧版 Acrylic 开关 → 新窗口主题（Aero）
            if (sb.WindowTheme == WindowTheme.None && sb.Acrylic)
            {
                sb.WindowTheme = WindowTheme.Aero;
                accentMigrated = true;
            }
        }
        if (accentMigrated) SaveNow();

        Config.AutoStart = GetAutoStart();
    }

    /// <summary>尝试把指定路径的配置解析进 Config；失败返回 false。</summary>
    private static bool TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            if (cfg == null) return false;
            Config = cfg;
            return true;
        }
        catch { return false; }
    }

    /// <summary>诊断日志（追加到 error.log）。</summary>
    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(Path.Combine(DataDir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\r\n");
        }
        catch { }
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
            string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
            // 原子写入：先写临时文件再替换，避免强杀/断电把 config.json 截断成损坏文件
            string tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(ConfigPath)) File.Replace(tmp, ConfigPath, null);
            else File.Move(tmp, ConfigPath);
        }
        catch (Exception ex)
        {
            // 原子写失败：回退为直接写（至少不丢配置）
            try
            {
                File.WriteAllText(ConfigPath,
                    JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
            Log($"保存配置失败: {ex}");
        }
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
        // 主机制：HKCU Run 注册表项
        try
        {
            var exe = CurrentExePath();
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enable) key?.SetValue("EdgeShelf", $"\"{exe}\"");
            else key?.DeleteValue("EdgeShelf", false);
        }
        catch { }

        // 备份机制：启动文件夹快捷方式（与 Run 键双保险；单实例互斥保证不会启动两份）
        try
        {
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string lnkPath = Path.Combine(startup, "EdgeShelf.lnk");
            if (enable)
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                dynamic lnk = shell.CreateShortcut(lnkPath);
                lnk.TargetPath = CurrentExePath();
                lnk.Save();
            }
            else if (File.Exists(lnkPath))
            {
                File.Delete(lnkPath);
            }
        }
        catch { }

        // 第三机制：任务计划程序（登录触发）——由系统服务触发，不依赖 Explorer 的启动项处理，最可靠
        try
        {
            var exe = CurrentExePath();
            string args = enable
                ? $"/Create /TN \"EdgeShelf\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL LIMITED /F"
                : "/Delete /TN \"EdgeShelf\" /F";
            var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(10000);
        }
        catch { }
    }

    private static string CurrentExePath()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            exe = Path.Combine(AppContext.BaseDirectory, "EdgeShelf.exe");
        return exe;
    }
}
