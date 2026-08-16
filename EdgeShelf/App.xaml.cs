using System.IO;
using System.Threading;
using System.Windows;
using EdgeShelf.Models;
using EdgeShelf.Services;
using EdgeShelf.Views;

namespace EdgeShelf;

public partial class App : Application
{
    private Mutex? _mutex;
    private TrayIcon? _tray;
    private readonly List<MainWindow> _windows = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            StartupCore(e);
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(ConfigService.DataDir);
                File.WriteAllText(Path.Combine(ConfigService.DataDir, "startup-error.log"), ex.ToString());
            }
            catch { }
            throw;
        }
    }

    private void StartupCore(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "EdgeShelf.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // 互斥体已存在：可能是另一实例在运行，也可能是被强杀遗留的 abandoned 状态。
            // WaitOne(0) 拿到所有权 = 之前的实例已退出/被强杀，我们接管并继续启动；
            // 拿不到 = 有实例正在运行，退出。
            try
            {
                if (!_mutex.WaitOne(0))
                {
                    Shutdown();
                    return;
                }
            }
            catch (AbandonedMutexException)
            {
                // 前一个实例异常退出，互斥体被放弃：当前实例已获得所有权，继续启动
            }
        }

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                Directory.CreateDirectory(ConfigService.DataDir);
                File.AppendAllText(Path.Combine(ConfigService.DataDir, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {args.Exception}\r\n\r\n");
            }
            catch { }
        };

        ConfigService.Load();

        // 启动默认正常模式：透明 / 无痕启动时蓝条不可见、贴边也不弹，容易误以为没打开。
        // 每次启动都回到正常模式（不写回配置，会话内切换模式仍会保存）
        foreach (var sb in ConfigService.Config.Sidebars)
        {
            sb.Mode = DockMode.Normal;
            foreach (var t in sb.Tabs) t.Mode = DockMode.Normal;
        }

        // 启动日志：确认应用是否真的被启动（开机自启诊断用）
        try
        {
            Directory.CreateDirectory(ConfigService.DataDir);
            File.AppendAllText(Path.Combine(ConfigService.DataDir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] EdgeShelf 启动\r\n");
        }
        catch { }

        // 自启自愈：后台执行——schtasks / WScript.Shell 可能慢或卡住，绝不能阻塞启动
        if (ConfigService.Config.AutoStart)
        {
            System.Threading.Tasks.Task.Run(() => ConfigService.SetAutoStart(true));
        }

        foreach (var cfg in ConfigService.Config.Sidebars)
        {
            var w = CreateWindow(cfg);
            w.Show();
        }

        _tray = new TrayIcon(() => _windows,
            PromptNewSidebar,
            RequestExit);
        _tray.Install();
        foreach (var w in _windows) w.Tray = _tray;

        ApplyHotkey();

        // 调试用：EdgeShelf.exe --settings 启动后直接打开第一个侧边栏的设置
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase) && _windows.Count > 0)
            _windows[0].OpenSettings();
    }

    private MainWindow CreateWindow(SidebarConfig cfg)
    {
        var w = new MainWindow(cfg);
        _windows.Add(w);
        w.NewSidebarRequested += () => PromptNewSidebar();
        w.DeleteSidebarRequested += () => RemoveSidebar(w);
        w.MergeRequested += target => MergeSidebars(w, target);
        w.UnmergeRequested += tab => UnmergeTab(w, tab);
        w.MergeTabRequested += (tab, target) => MergeTabTo(w, tab, target);
        w.GlobalHotkeyPressed += OnGlobalHotkey;
        w.HotkeyReapplyRequested += ApplyHotkey;
        return w;
    }

    /// <summary>全局快捷键：按序循环切换已勾选模式（普→透→无→固，对所有侧边栏生效）。</summary>
    private void OnGlobalHotkey()
    {
        foreach (var w in _windows) w.AdvanceMode();
    }

    /// <summary>新建侧边栏：让用户选择 独立建立 或 作为页签加入现有侧边栏。</summary>
    private void PromptNewSidebar()
    {
        var dlg = new NewSidebarDialog(ConfigService.Config.Sidebars) { Owner = _windows.Count > 0 ? _windows[0] : null };
        if (dlg.ShowDialog() != true) return;

        if (dlg.AsTab && dlg.Target != null)
        {
            dlg.Target.Tabs.Add(new SidebarConfig { Name = NextSidebarName(dlg.Target.Tabs.Select(t => t.Name)) });
            var host = _windows.FirstOrDefault(w => ReferenceEquals(w.SidebarConfig, dlg.Target));
            if (host != null)
            {
                host.RefreshTabs();
                host.SelectLastTab();
            }
        }
        else
        {
            var sb = new SidebarConfig { Name = NextSidebarName(ConfigService.Config.Sidebars.Select(s => s.Name)) };
            ConfigService.Config.Sidebars.Add(sb);
            var w = CreateWindow(sb);
            w.Show();
            w.Tray = _tray;
        }
        ConfigService.Save();
        _tray?.Refresh();
    }

    private static string NextSidebarName(IEnumerable<string> existing)
    {
        int n = 1;
        while (existing.Any(x => string.Equals(x, $"侧边栏 {n}", StringComparison.Ordinal))) n++;
        return $"侧边栏 {n}";
    }

    /// <summary>根据配置注册/注销全局快捷键（注册在第一个窗口上）。</summary>
    public void ApplyHotkey()
    {
        foreach (var w in _windows) w.UnregisterHotkey();
        var cfg = ConfigService.Config;
        if (!cfg.HotkeyEnabled || cfg.HotkeyKey == 0 || _windows.Count == 0) return;
        _windows[0].RegisterHotkey(cfg.HotkeyModifiers, cfg.HotkeyKey);
    }

    /// <summary>把一个页签合并进另一个侧边栏（变成它的页签）。</summary>
    private void MergeTabTo(MainWindow host, SidebarConfig tab, MainWindow target)
    {
        host.SidebarConfig.Tabs.Remove(tab);
        target.SidebarConfig.Tabs.Add(tab);
        target.SelectLastTab();
        host.RefreshTabs();
        host.RefreshGroups();
        _tray?.Refresh();
        ConfigService.Save();
    }

    /// <summary>把 from 侧边栏合并成 to 侧边栏的一个页签（拖动蓝色栏互相命中时）。</summary>
    private void MergeSidebars(MainWindow from, MainWindow to)
    {
        if (from == to) return;
        to.SidebarConfig.Tabs.Add(from.SidebarConfig);
        foreach (var t in from.SidebarConfig.Tabs) to.SidebarConfig.Tabs.Add(t);
        from.SidebarConfig.Tabs.Clear();

        _windows.Remove(from);
        ConfigService.Config.Sidebars.Remove(from.SidebarConfig);
        from.PrepareClose();
        from.Close();

        to.SelectLastTab();
        _tray?.Refresh();
        ApplyHotkey();
        ConfigService.Save();
    }

    /// <summary>把页签拖出面板，分离成独立侧边栏。</summary>
    private void UnmergeTab(MainWindow host, SidebarConfig tab)
    {
        host.SidebarConfig.Tabs.Remove(tab);
        ConfigService.Config.Sidebars.Add(tab);
        var w = CreateWindow(tab);
        w.Show();
        w.Tray = _tray;
        host.RefreshTabs();
        host.RefreshGroups();
        _tray?.Refresh();
        ApplyHotkey();
        ConfigService.Save();
    }

    private void RemoveSidebar(MainWindow w)
    {
        w.PrepareClose();
        _windows.Remove(w);
        ConfigService.Config.Sidebars.Remove(w.SidebarConfig);
        w.Close();
        if (_windows.Count == 0)
        {
            // 保留一个空侧边栏，避免程序无可显示内容
            var fresh = new SidebarConfig { Name = "侧边栏 1" };
            ConfigService.Config.Sidebars.Add(fresh);
            var nw = CreateWindow(fresh);
            nw.Show();
            nw.Tray = _tray;
        }
        _tray?.Refresh();
        ApplyHotkey();
        ConfigService.Save();
    }

    private void RequestExit()
    {
        foreach (var w in _windows) w.PrepareClose();
        ConfigService.SaveNow();
        Application.Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        ConfigService.SaveNow();
        base.OnExit(e);
    }
}
