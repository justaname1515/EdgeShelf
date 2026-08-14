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
            // 已有实例在运行，直接退出
            Shutdown();
            return;
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

        foreach (var cfg in ConfigService.Config.Sidebars)
        {
            var w = CreateWindow(cfg);
            w.Show();
        }

        _tray = new TrayIcon(() => _windows,
            () => { if (_windows.Count > 0) _windows[0].AddTab(); },
            RequestExit);
        _tray.Install();
        foreach (var w in _windows) w.Tray = _tray;

        // 调试用：EdgeShelf.exe --settings 启动后直接打开第一个侧边栏的设置
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase) && _windows.Count > 0)
            _windows[0].OpenSettings();
    }

    private MainWindow CreateWindow(SidebarConfig cfg)
    {
        var w = new MainWindow(cfg);
        _windows.Add(w);
        w.NewSidebarRequested += () => w.AddTab();
        w.DeleteSidebarRequested += () => RemoveSidebar(w);
        w.MergeRequested += target => MergeSidebars(w, target);
        w.UnmergeRequested += tab => UnmergeTab(w, tab);
        w.MergeTabRequested += (tab, target) => MergeTabTo(w, tab, target);
        return w;
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
