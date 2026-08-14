using System.Runtime.InteropServices;

namespace EdgeShelf.Services;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
}

public class MonitorInfo
{
    public IntPtr Handle { get; set; }
    public RECT Bounds { get; set; }
    public RECT WorkArea { get; set; }
    public double Scale { get; set; } = 1.0;
    public bool Primary { get; set; }
    public int Index { get; set; }
    public string Label => $"屏幕 {Index + 1}  ({Width}×{Height})";
    public double Width => Bounds.Right - Bounds.Left;
    public double Height => Bounds.Bottom - Bounds.Top;
    public double WorkWidth => WorkArea.Right - WorkArea.Left;
    public double WorkHeight => WorkArea.Bottom - WorkArea.Top;
}

public static class ScreenManager
{
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITORINFOF_PRIMARY = 1;

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    private static List<MonitorInfo>? _cache;

    public static List<MonitorInfo> Monitors => _cache ??= BuildList();

    public static void Refresh() => _cache = BuildList();

    private static List<MonitorInfo> BuildList()
    {
        var list = new List<MonitorInfo>();
        var raw = new List<(IntPtr handle, RECT rect)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr h, IntPtr _, ref RECT rect, IntPtr _2) => { raw.Add((h, rect)); return true; }, IntPtr.Zero);

        int idx = 0;
        foreach (var (handle, rect) in raw)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(handle, ref mi);
            GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
            list.Add(new MonitorInfo
            {
                Handle = handle,
                Bounds = rect,
                WorkArea = mi.rcWork,
                Scale = (dpiX == 0 ? 96 : dpiX) / 96.0,
                Primary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                Index = idx++
            });
        }

        if (list.Count == 0)
        {
            // 兜底：假设单屏 1920×1080
            list.Add(new MonitorInfo
            {
                Handle = IntPtr.Zero,
                Bounds = new RECT { Right = 1920, Bottom = 1080 },
                WorkArea = new RECT { Right = 1920, Bottom = 1040 },
                Scale = 1.0,
                Primary = true,
                Index = 0
            });
        }
        return list;
    }

    public static MonitorInfo FromPoint(int x, int y)
    {
        var h = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        foreach (var m in Monitors)
            if (m.Handle == h) return m;
        return Monitors[0];
    }
}
