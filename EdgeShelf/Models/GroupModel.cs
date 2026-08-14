using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using EdgeShelf.Services;

namespace EdgeShelf.Models;

public class ItemInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; } = -1;
    public DateTime Modified { get; set; }
    public ImageSource? Icon { get; set; }
    public string SizeText => Size < 0 ? "" : FileSizeFormatter.Format(Size);
    public string TypeText => IsDirectory ? "文件夹" : (System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant() + " 文件");
    public string ModifiedText => Modified == DateTime.MinValue ? "" : Modified.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>
/// 一个「抽屉」：对应磁盘上的一个文件夹。
/// 支持钻入子文件夹查看（内部分组），以及折叠/展开。
/// </summary>
public class GroupModel : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";

    private string _currentPath = "";
    /// <summary>当前显示的位置（钻入子文件夹后为其路径）。</summary>
    public string CurrentPath
    {
        get => _currentPath;
        private set { _currentPath = value; OnPropertyChanged(); }
    }

    public bool IsDrilled => !string.Equals(CurrentPath, Path, StringComparison.OrdinalIgnoreCase);

    private bool _isCollapsed;
    public bool IsCollapsed
    {
        get => _isCollapsed;
        set { _isCollapsed = value; OnPropertyChanged(); }
    }

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); }
    }

    private string _itemCountText = "";
    public string ItemCountText
    {
        get => _itemCountText;
        set { _itemCountText = value; OnPropertyChanged(); }
    }

    private string _searchText = "";
    public string SearchText => _searchText;
    public bool SearchActive => _searchText.Length > 0;
    public bool SearchNoMatch => SearchActive && Items.Count == 0;

    public ObservableCollection<ItemInfo> Items { get; } = new();
    private readonly List<ItemInfo> _allItems = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>进入子文件夹（内部分组）。</summary>
    public void NavigateTo(string subPath)
    {
        if (Directory.Exists(subPath))
        {
            CurrentPath = subPath;
            RefreshItems();
            OnPropertyChanged(nameof(IsDrilled));
        }
    }

    /// <summary>返回抽屉根目录。</summary>
    public void NavigateBack()
    {
        CurrentPath = Path;
        RefreshItems();
        OnPropertyChanged(nameof(IsDrilled));
    }

    public void RefreshItems()
    {
        if (string.IsNullOrEmpty(CurrentPath)) CurrentPath = Path;

        _allItems.Clear();
        ItemCountText = "…";
        try
        {
            if (!Directory.Exists(CurrentPath))
            {
                ItemCountText = "文件夹不存在";
                ApplySearch(_searchText);
                return;
            }

            foreach (var d in Directory.EnumerateDirectories(CurrentPath))
            {
                _allItems.Add(new ItemInfo
                {
                    Name = System.IO.Path.GetFileName(d),
                    DisplayName = System.IO.Path.GetFileName(d),
                    Path = d,
                    IsDirectory = true
                });
            }
            foreach (var f in Directory.EnumerateFiles(CurrentPath))
            {
                var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                bool isShortcut = ext is ".lnk" or ".url";
                _allItems.Add(new ItemInfo
                {
                    Name = System.IO.Path.GetFileName(f),
                    DisplayName = isShortcut ? System.IO.Path.GetFileNameWithoutExtension(f) : System.IO.Path.GetFileName(f),
                    Path = f
                });
            }

            // 文件夹优先，其次是快捷方式，再是普通文件；同类按名称排序
            _allItems.Sort((a, b) =>
            {
                int rankA = RankOf(a), rankB = RankOf(b);
                if (rankA != rankB) return rankA.CompareTo(rankB);
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var it in _allItems)
            {
                it.Icon = ShellIcon.GetIcon(it.Path, small: false);
            }
            ItemCountText = $"{_allItems.Count} 项";
        }
        catch
        {
            ItemCountText = "无法访问";
        }
        Icon = ShellIcon.GetIcon(Path, small: false);
        ApplySearch(_searchText);
        OnPropertyChanged(nameof(IsDrilled));
    }

    /// <summary>按名称过滤抽屉里的内容（搜索）。</summary>
    public void ApplySearch(string query)
    {
        _searchText = query?.Trim() ?? "";
        Items.Clear();
        foreach (var it in _allItems)
        {
            if (_searchText.Length == 0 ||
                it.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                it.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            {
                Items.Add(it);
            }
        }
        if (SearchActive) IsCollapsed = false; // 搜索时自动展开抽屉
        OnPropertyChanged(nameof(SearchActive));
        OnPropertyChanged(nameof(SearchNoMatch));
    }

    private static int RankOf(ItemInfo it)
    {
        if (it.IsDirectory) return 0;
        var ext = System.IO.Path.GetExtension(it.Path).ToLowerInvariant();
        if (ext is ".lnk" or ".url") return 1;
        return 2;
    }
}
