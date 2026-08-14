using System.Runtime.InteropServices;
using System.Windows.Media;

namespace EdgeShelf.Services;

/// <summary>Win10/11 亚克力/模糊背景特效。</summary>
public static class WindowEffects
{
    public enum AccentState
    {
        Disabled = 0,
        EnableGradient = 1,
        EnableTransparentGradient = 2,
        EnableBlurBehind = 3,
        EnableAcrylicBlurBehind = 4,
        EnableHostBackdrop = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    private const int WCA_ACCENT_POLICY = 19;

    /// <summary>应用亚克力模糊，成功返回 true。</summary>
    public static bool TrySetAcrylic(IntPtr hwnd, Color tint, double tintOpacity)
    {
        byte a = (byte)Math.Clamp((int)(tintOpacity * 255), 0, 255);
        uint abgr = ((uint)a << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R;
        return Apply(hwnd, new AccentPolicy
        {
            AccentState = (int)AccentState.EnableAcrylicBlurBehind,
            AccentFlags = 2,
            GradientColor = abgr,
            AnimationId = 0
        });
    }

    public static bool TrySetBlur(IntPtr hwnd)
        => Apply(hwnd, new AccentPolicy { AccentState = (int)AccentState.EnableBlurBehind, AccentFlags = 2, GradientColor = 0 });

    public static void Reset(IntPtr hwnd)
        => Apply(hwnd, new AccentPolicy { AccentState = (int)AccentState.Disabled, AccentFlags = 2, GradientColor = 0 });

    private static bool Apply(IntPtr hwnd, AccentPolicy accent)
    {
        try
        {
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>()),
                SizeOfData = Marshal.SizeOf<AccentPolicy>()
            };
            try
            {
                Marshal.StructureToPtr(accent, data.Data, false);
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(data.Data);
            }
        }
        catch { return false; }
    }
}
