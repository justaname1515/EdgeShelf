using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EdgeShelf.Models;
using EdgeShelf.Services;

namespace EdgeShelf.Views;

public partial class GroupsView : UserControl
{
    public event EventHandler? AddGroupRequested;
    public event EventHandler<GroupModel>? RenameRequested;
    public event EventHandler<GroupModel>? DeleteRequested;
    public event EventHandler<GroupModel>? NewSubfolderRequested;
    public event Action<ItemInfo, GroupModel>? DeleteItemRequested;
    public event Action<ItemInfo, GroupModel, GroupModel>? MoveItemRequested;

    /// <summary>供主窗口注入：给定源抽屉，返回可移动到的其他抽屉列表。</summary>
    public Func<GroupModel, IEnumerable<GroupModel>>? DrawersProvider { get; set; }

    public GroupsView()
    {
        InitializeComponent();
    }

    public void SetGroups(IEnumerable<GroupModel> groups)
    {
        var list = groups.ToList();
        GroupsList.ItemsSource = list;
        EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>从点击的元素向上找到所在分组的 GroupModel。</summary>
    private static GroupModel? FindGroup(DependencyObject? start)
    {
        while (start != null)
        {
            if (start is FrameworkElement fe && fe.DataContext is GroupModel g) return g;
            start = VisualTreeHelper.GetParent(start);
        }
        return null;
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        var dc = (e.OriginalSource as FrameworkElement)?.DataContext;
        if (dc is GroupModel or ItemInfo) return; // 点在卡片/图标上不算空白
        AddGroupRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NameRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GroupModel g) return;
        if (g.IsDrilled) g.NavigateBack();
        else g.IsCollapsed = !g.IsCollapsed;
    }

    private void Item_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ItemInfo it) return;
        if (it.IsDirectory)
        {
            // 内部分组：点击子文件夹钻入
            if (FindGroup(sender as DependencyObject) is GroupModel g)
            {
                g.NavigateTo(it.Path);
                e.Handled = true;
            }
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(it.Path) { UseShellExecute = true });
        }
        catch { }
    }

    // ---------------- 手动排序（按住图标拖动） ----------------

    private GroupModel? _dragGroup;
    private ItemInfo? _dragItem;
    private ItemsControl? _dragHost;
    private Point _pressPoint;
    private bool _dragging;

    private static ItemInfo? FindItemAt(object? originalSource)
    {
        var el = originalSource as DependencyObject;
        while (el != null)
        {
            if (el is FrameworkElement fe && fe.DataContext is ItemInfo it) return it;
            el = VisualTreeHelper.GetParent(el);
        }
        return null;
    }

    private void Tiles_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var host = sender as ItemsControl;
        var it = FindItemAt(e.OriginalSource);
        var g = host != null ? FindGroup(host) : null;
        if (it == null || g == null || g.SearchActive)
        {
            _dragItem = null;
            return;
        }
        _dragGroup = g;
        _dragItem = it;
        _dragHost = host;
        _pressPoint = e.GetPosition(null);
        _dragging = false;
    }

    private void Tiles_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem == null || _dragGroup == null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragItem = null;
            return;
        }
        if (!_dragging)
        {
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _pressPoint.X) < 6 && Math.Abs(pos.Y - _pressPoint.Y) < 6) return;
            _dragging = true;
            _dragHost?.CaptureMouse();
        }
        e.Handled = true; // 进入拖拽后不再触发按钮点击
    }

    private void Tiles_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var wasDragging = _dragging;
        _dragging = false;
        var host = _dragHost;
        var g = _dragGroup;
        var dragged = _dragItem;
        _dragItem = null;

        if (!wasDragging) return;

        e.Handled = true;
        host?.ReleaseMouseCapture();
        if (host == null || g == null || dragged == null) return;

        int oldIndex = g.Items.IndexOf(dragged);
        if (oldIndex < 0) return;

        var pos = e.GetPosition(host);
        int target = GetDropIndex(host, pos, g.Items.Count);
        var list = g.Items.ToList();
        list.RemoveAt(oldIndex);
        if (target > oldIndex) target--;
        target = Math.Clamp(target, 0, list.Count);
        list.Insert(target, dragged);

        g.SetItemOrder(list);
        ConfigService.Save();
    }

    private static int GetDropIndex(ItemsControl itemsControl, Point pos, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (itemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            var p = c.TranslatePoint(new Point(0, 0), itemsControl);
            if (pos.X >= p.X && pos.X <= p.X + c.ActualWidth &&
                pos.Y >= p.Y && pos.Y <= p.Y + c.ActualHeight)
            {
                return pos.X < p.X + c.ActualWidth / 2 ? i : i + 1;
            }
        }
        return count; // 空白处 → 末尾
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GroupModel g) g.NavigateBack();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GroupModel g) OpenInExplorer(g.CurrentPath);
    }

    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border b) b.Background = (Brush)FindResource("CardHoverBrush");
    }

    private void Card_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border b) b.Background = (Brush)FindResource("CardBrush");
    }

    private void CtxOpen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GroupModel g) OpenInExplorer(g.CurrentPath);
    }

    private void CtxNewSub_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GroupModel g)
            NewSubfolderRequested?.Invoke(this, g);
    }

    private void CtxRename_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GroupModel g)
            RenameRequested?.Invoke(this, g);
    }

    private void CtxDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GroupModel g)
            DeleteRequested?.Invoke(this, g);
    }

    public static void OpenInExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    // ---------------- 瓦片右键菜单（删除 / 移动） ----------------

    private void Tile_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ItemInfo it) return;
        if (FindGroup(sender as DependencyObject) is not GroupModel src) return;
        var menu = (sender as FrameworkElement)?.ContextMenu;
        if (menu == null) return;

        var deleteMenu = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header as string == "删除（移入回收站）");
        var moveMenu = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header as string == "移动到其他抽屉…");
        if (deleteMenu != null) deleteMenu.Visibility = it.IsDirectory ? Visibility.Collapsed : Visibility.Visible;
        if (moveMenu == null) return;

        moveMenu.Items.Clear();
        var targets = DrawersProvider?.Invoke(src)?.Where(g => !ReferenceEquals(g, src)).ToList() ?? new List<GroupModel>();
        if (targets.Count == 0)
        {
            moveMenu.Items.Add(new MenuItem { Header = "（没有其他抽屉）", IsEnabled = false });
        }
        else
        {
            foreach (var g in targets)
            {
                var item = new MenuItem { Header = g.Name };
                var target = g;
                item.Click += (_, _) => MoveItemRequested?.Invoke(it, src, target);
                moveMenu.Items.Add(item);
            }
        }
    }

    private void MoveTo_Click(object sender, RoutedEventArgs e)
    {
        // 实际移动由子菜单项触发（MoveItemRequested），此入口保留为空以防误触发
        if (sender is MenuItem m && m.Items.Count == 0) e.Handled = true;
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not ItemInfo it) return;
        if (FindGroupFromMenu(sender as MenuItem) is not GroupModel g || it.IsDirectory) return;
        DeleteItemRequested?.Invoke(it, g);
    }

    /// <summary>从 ContextMenu 的 PlacementTarget 找到所在分组。</summary>
    private static GroupModel? FindGroupFromMenu(MenuItem? item)
    {
        if (item?.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement fe)
            return FindGroup(fe);
        return null;
    }
}
