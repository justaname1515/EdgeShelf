using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EdgeShelf.Services;

/// <summary>通过 Shell API 提取文件/文件夹/快捷方式的图标。</summary>
public static class ShellIcon
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    private static readonly Dictionary<string, ImageSource> Cache = new();
    private static readonly object CacheLock = new();

    public static ImageSource GetIcon(string path, bool small = false)
    {
        string key = (small ? "s:" : "l:") + path.ToLowerInvariant();
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        var icon = Extract(path, small) ?? ExtractGeneric(path, small);
        if (icon == null)
        {
            icon = small
                ? new DrawingImage(new GeometryDrawing(Brushes.Transparent, null, Geometry.Empty))
                : new DrawingImage(new GeometryDrawing(Brushes.Transparent, null, Geometry.Empty));
        }
        icon.Freeze();

        lock (CacheLock)
        {
            if (!Cache.ContainsKey(key)) Cache[key] = icon;
        }
        return icon;
    }

    private static ImageSource? Extract(string path, bool small)
    {
        try
        {
            var sfi = new SHFILEINFO();
            uint flags = SHGFI_ICON | (small ? SHGFI_SMALLICON : SHGFI_LARGEICON);
            IntPtr r = SHGetFileInfo(path, 0, ref sfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (r == IntPtr.Zero || sfi.hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(sfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                return src;
            }
            finally
            {
                DestroyIcon(sfi.hIcon);
            }
        }
        catch { return null; }
    }

    private static ImageSource? ExtractGeneric(string path, bool small)
    {
        try
        {
            var sfi = new SHFILEINFO();
            bool isDir = Directory.Exists(path);
            uint attrs = isDir ? FILE_ATTRIBUTE_DIRECTORY : 0;
            uint flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | (small ? SHGFI_SMALLICON : SHGFI_LARGEICON);
            SHGetFileInfo(path, attrs, ref sfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (sfi.hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(sfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                return src;
            }
            finally
            {
                DestroyIcon(sfi.hIcon);
            }
        }
        catch { return null; }
    }
}
