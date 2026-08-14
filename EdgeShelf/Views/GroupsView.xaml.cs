using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EdgeShelf.Models;

namespace EdgeShelf.Views;

public partial class GroupsView : UserControl
{
    public event EventHandler? AddGroupRequested;
    public event EventHandler<GroupModel>? RenameRequested;
    public event EventHandler<GroupModel>? DeleteRequested;
    public event EventHandler<GroupModel>? NewSubfolderRequested;

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
}
