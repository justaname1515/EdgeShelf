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
    public event Action<ItemInfo, GroupModel>? RenameItemRequested;

    /// <summary>抽屉移动方向（右键菜单排序）。</summary>
    public enum DrawerMove { Up, Down, Top, Bottom }
    public event Action<GroupModel, DrawerMove>? MoveDrawerRequested;

    /// <summary>供主窗口注入：给定源抽屉，返回可移动到的其他抽屉列表。</summary>
    public Func<GroupModel, IEnumerable<GroupModel>>? DrawersProvider { get; set; }

    private bool _listMode;

    public GroupsView()
    {
        InitializeComponent();
    }

    public void SetGroups(IEnumerable<GroupModel> groups)
    {
        var list = groups.ToList();
        GroupsList.ItemsSource = list;
        foreach (var g in list) g.SetListMode(_listMode);
        EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>切换宫格 / 列表视图（对所有抽屉生效）。</summary>
    public void SetViewMode(bool list)
    {
        _listMode = list;
        if (GroupsList.ItemsSource is IEnumerable<GroupModel> groups)
            foreach (var g in groups) g.SetListMode(list);
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
            // 内部分组：点击子文件夹钻入（仅宫格视图；列表视图用内联展开）
            if (FindGroup(sender as DependencyObject) is GroupModel g)
            {
                g.NavigateTo(it.Path);
                e.Handled = true;
            }
            return;
        }
        OpenItem(it);
    }

    /// <summary>列表视图点击：文件夹内联展开 / 收起（不钻入），文件直接打开。</summary>
    private void ListRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ListRow row) return;
        var it = row.Item;
        if (it.IsDirectory)
        {
            if (FindGroup(sender as DependencyObject) is GroupModel g)
            {
                g.ToggleExpand(it);
                e.Handled = true;
            }
            return;
        }
        OpenItem(it);
    }

    private static void OpenItem(ItemInfo it)
    {
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

    private static ListRow? FindListRowAt(object? originalSource)
    {
        var el = originalSource as DependencyObject;
        while (el != null)
        {
            if (el is FrameworkElement fe && fe.DataContext is ListRow row) return row;
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

    // ---------------- 列表模式手动排序（拖动顶层行；子行按树归属不参与） ----------------

    private GroupModel? _listDragGroup;
    private ItemInfo? _listDragItem;      // 被拖的顶层项
    private ItemsControl? _listDragHost;
    private Point _listPressPoint;
    private bool _listDragging;

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var host = sender as ItemsControl;
        var row = FindListRowAt(e.OriginalSource);
        var g = host != null ? FindGroup(host) : null;
        if (row == null || g == null || g.SearchActive || row.Depth != 0)
        {
            _listDragGroup = null;
            return;
        }
        _listDragGroup = g;
        _listDragItem = row.Item;
        _listDragHost = host;
        _listPressPoint = e.GetPosition(null);
        _listDragging = false;
    }

    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_listDragGroup == null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _listDragGroup = null;
            return;
        }
        if (!_listDragging)
        {
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _listPressPoint.X) < 6 && Math.Abs(pos.Y - _listPressPoint.Y) < 6) return;
            _listDragging = true;
            _listDragHost?.CaptureMouse();
        }
        e.Handled = true; // 进入拖拽后不再触发行的点击（展开/打开）
    }

    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var wasDragging = _listDragging;
        _listDragging = false;
        var host = _listDragHost;
        var g = _listDragGroup;
        var dragged = _listDragItem;
        _listDragGroup = null;
        _listDragItem = null;

        if (!wasDragging) return;

        e.Handled = true;
        host?.ReleaseMouseCapture();
        if (host == null || g == null || dragged == null) return;

        int oldTop = g.Items.IndexOf(dragged);
        if (oldTop < 0) return;

        var pos = e.GetPosition(host);
        // 鼠标捕获期间 OriginalSource 是宿主本身，必须按坐标找行
        var hit = FindListRowAtPoint(host, pos);
        int targetTop;
        if (hit == null)
        {
            targetTop = g.Items.Count; // 空白处 → 末尾
        }
        else
        {
            var (dropRow, listIdx) = hit.Value;
            if (dropRow == null)
            {
                targetTop = g.Items.Count;
            }
            else
            {
                // 目标行（可能是子行）归属的顶层索引
                targetTop = TopLevelIndexOf(dropRow, g);
                if (host.ItemContainerGenerator.ContainerFromIndex(listIdx) is FrameworkElement c)
                {
                    var p = c.TranslatePoint(new Point(0, 0), host);
                    if (pos.Y > p.Y + c.ActualHeight / 2) targetTop++;
                }
            }
        }
        var list = g.Items.ToList();
        list.RemoveAt(oldTop);
        if (targetTop > oldTop) targetTop--;
        targetTop = Math.Clamp(targetTop, 0, list.Count);
        list.Insert(targetTop, dragged);

        g.SetItemOrder(list); // 内部会重建列表行
        ConfigService.Save();
    }

    /// <summary>按坐标在列表容器中命中行（返回行与它在 ListRows 中的索引）。</summary>
    private static (ListRow? row, int listIdx)? FindListRowAtPoint(ItemsControl host, Point pos)
    {
        for (int i = 0; i < host.Items.Count; i++)
        {
            if (host.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            var p = c.TranslatePoint(new Point(0, 0), host);
            if (pos.Y >= p.Y && pos.Y <= p.Y + c.ActualHeight)
                return (c.DataContext as ListRow, i);
        }
        return null;
    }

    /// <summary>ListRow 在顶层（Depth==0）序列中的索引。</summary>
    private static int TopLevelIndexOf(ListRow row, GroupModel g)
    {
        int idx = g.ListRows.IndexOf(row);
        if (idx < 0) return 0;
        int top = 0;
        for (int i = 0; i <= idx; i++)
            if (g.ListRows[i].Depth == 0) top++;
        return top - 1;
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

    // ---------------- 抽屉右键菜单（排序 / 删除） ----------------

    private void CtxMoveUp_Click(object sender, RoutedEventArgs e)
        => CtxMove(sender, DrawerMove.Up);

    private void CtxMoveDown_Click(object sender, RoutedEventArgs e)
        => CtxMove(sender, DrawerMove.Down);

    private void CtxMoveTop_Click(object sender, RoutedEventArgs e)
        => CtxMove(sender, DrawerMove.Top);

    private void CtxMoveBottom_Click(object sender, RoutedEventArgs e)
        => CtxMove(sender, DrawerMove.Bottom);

    private void CtxMove(object sender, DrawerMove move)
    {
        if ((sender as FrameworkElement)?.DataContext is GroupModel g)
            MoveDrawerRequested?.Invoke(g, move);
    }

    // ---------------- 瓦片 / 列表行右键菜单（重命名 / 移动 / 删除） ----------------

    /// <summary>从 DataContext 解析 ItemInfo（宫格=ItemInfo，列表=ListRow）。</summary>
    private static ItemInfo? MenuItemInfo(FrameworkElement? fe) => fe?.DataContext switch
    {
        ItemInfo i => i,
        ListRow r => r.Item,
        _ => null
    };

    private void Tile_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var fe = sender as FrameworkElement;
        if (MenuItemInfo(fe) is not ItemInfo it) return;
        if (FindGroup(fe) is not GroupModel src) return;
        var menu = fe?.ContextMenu;
        if (menu == null) return;

        var moveMenu = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header as string == "移动到其他抽屉…");
        // 文件夹与文件一视同仁：都提供 重命名 / 移动 / 删除
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

    private void RenameItem_Click(object sender, RoutedEventArgs e)
    {
        var mi = sender as MenuItem;
        if (MenuItemInfo(mi) is not ItemInfo it) return;
        if (FindGroupFromMenu(mi) is not GroupModel g) return;
        RenameItemRequested?.Invoke(it, g);
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        var mi = sender as MenuItem;
        if (MenuItemInfo(mi) is not ItemInfo it) return;
        if (FindGroupFromMenu(mi) is not GroupModel g || it.IsDirectory) return;
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
