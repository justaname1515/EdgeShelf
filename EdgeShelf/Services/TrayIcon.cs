using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EdgeShelf.Views;

namespace EdgeShelf.Services;

/// <summary>系统托盘图标与右键菜单（适配多侧边栏）。</summary>
public class TrayIcon : IDisposable
{
    private readonly Func<IReadOnlyList<MainWindow>> _getWindows;
    private readonly Action _onNewSidebar;
    private readonly Action _onExit;
    private NotifyIcon? _notify;
    private Icon? _icon;

    public TrayIcon(Func<IReadOnlyList<MainWindow>> getWindows, Action onNewSidebar, Action onExit)
    {
        _getWindows = getWindows;
        _onNewSidebar = onNewSidebar;
        _onExit = onExit;
    }

    public void Install()
    {
        _icon = LoadAppIcon();
        _notify = new NotifyIcon
        {
            Text = "EdgeShelf 边缘收纳",
            Icon = _icon,
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _notify.DoubleClick += (_, _) =>
        {
            var ws = _getWindows();
            if (ws.Count > 0) ws[0].TogglePanel();
        };
        Refresh();
    }

    /// <summary>侧边栏列表变化（新建/删除/改名）后重建菜单。</summary>
    public void Refresh()
    {
        if (_notify == null) return;
        var menu = _notify.ContextMenuStrip!;
        menu.SuspendLayout();
        menu.Items.Clear();

        menu.Items.Add(new ToolStripMenuItem("新建侧边栏（加到第一个侧边栏）", null, (_, _) => _onNewSidebar()));
        menu.Items.Add(new ToolStripSeparator());

        var windows = _getWindows();
        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            var sub = new ToolStripMenuItem($"{i + 1}. {w.SidebarConfig.Name}");
            sub.DropDownItems.Add(new ToolStripMenuItem("展开 / 收起面板", null, (_, _) => w.TogglePanel()));

            // 模式切换
            var modeMenu = new ToolStripMenuItem("切换模式");
            foreach (var (label, mode) in new[]
                     {
                         ("正常", EdgeShelf.Models.DockMode.Normal),
                         ("透明", EdgeShelf.Models.DockMode.Transparent),
                         ("无痕", EdgeShelf.Models.DockMode.Stealth)
                     })
            {
                var item = new ToolStripMenuItem(label) { Checked = w.SidebarConfig.Mode == mode };
                item.Click += (_, _) => w.SetMode(mode);
                modeMenu.DropDownItems.Add(item);
            }
            modeMenu.DropDownItems.Add(new ToolStripSeparator());
            var pinItem = new ToolStripMenuItem("固定面板")
            {
                CheckOnClick = true,
                Checked = w.SidebarConfig.Pinned
            };
            pinItem.Click += (_, _) => w.SetPinned(pinItem.Checked);
            modeMenu.DropDownItems.Add(pinItem);
            sub.DropDownItems.Add(modeMenu);

            sub.DropDownItems.Add(new ToolStripMenuItem("设置…", null, (_, _) => w.OpenSettings()));
            sub.DropDownItems.Add(new ToolStripMenuItem("删除", null, (_, _) => w.RequestDeleteSidebar()));
            menu.Items.Add(sub);
        }

        menu.Items.Add(new ToolStripSeparator());
        var autoStartItem = new ToolStripMenuItem("开机自启")
        {
            CheckOnClick = true,
            Checked = ConfigService.Config.AutoStart
        };
        autoStartItem.Click += (_, _) =>
        {
            ConfigService.Config.AutoStart = autoStartItem.Checked;
            ConfigService.SetAutoStart(autoStartItem.Checked);
            ConfigService.Save();
        };
        menu.Items.Add(autoStartItem);
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => _onExit()));

        menu.ResumeLayout();
    }

    /// <summary>从嵌入资源加载应用图标（16×16 托盘尺寸），失败则回退为自绘图标。</summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/App.ico"));
            if (info == null) return CreateIcon();
            using var stream = info.Stream;
            using var ico = new Icon(stream);
            return new Icon(ico, new Size(16, 16));
        }
        catch
        {
            return CreateIcon();
        }
    }

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(new RectangleF(1, 1, 30, 30), 8);
            using var bg = new SolidBrush(Color.FromArgb(76, 141, 255));
            g.FillPath(bg, path);
            using var fg = new SolidBrush(Color.White);
            g.FillRectangle(fg, 7, 9, 18, 3);
            g.FillRectangle(fg, 7, 15, 18, 3);
            g.FillRectangle(fg, 7, 21, 18, 3);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public void Dispose()
    {
        if (_notify != null)
        {
            _notify.Visible = false;
            _notify.Dispose();
            _notify = null;
        }
        if (_icon != null)
        {
            _icon.Dispose();
            _icon = null;
        }
    }
}
