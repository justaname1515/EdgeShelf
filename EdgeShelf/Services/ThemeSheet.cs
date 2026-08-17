using System.Windows.Media;
using EdgeShelf.Models;

namespace EdgeShelf.Services;

/// <summary>
/// 主题样式表（"CSS"式声明）：所有主题的默认配色与白天/黑夜处理规则集中定义在这里，
/// 设置页的色板填充与窗口渲染都读取同一份数据，改主题只动这一张表。
/// </summary>
public static class ThemeSheet
{
    /// <summary>单条主题规则。</summary>
    public sealed record ThemeRule(
        string Label,
        Color? BarDefault,      // 窄条默认色；null = 保持用户自定义
        Color? PanelDefault,    // 面板默认色
        bool? DayDefault,       // 默认白天/黑夜
        bool IsLightScheme,     // 浅色复古主题：黑夜下自动压暗，避免晃眼
        ControlPalette Controls); // 控件（按键）风格色板

    /// <summary>控件风格色板：按键渐变、边框、文字（各主题不同 → 按键也随主题换风格）。</summary>
    public sealed record ControlPalette(Color BtnTop, Color BtnBottom, Color BtnBorder, Color BtnText);

    private static readonly ControlPalette PalNone = new(
        Color.FromRgb(0x3E, 0x45, 0x54), Color.FromRgb(0x2A, 0x2F, 0x3B), Color.FromRgb(0x4A, 0x55, 0x68),
        Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly ControlPalette PalMica = new(
        Color.FromRgb(0x3A, 0x3F, 0x47), Color.FromRgb(0x2B, 0x2F, 0x36), Color.FromRgb(0x4A, 0x4F, 0x58),
        Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly ControlPalette PalAero = new(
        Color.FromRgb(0x4A, 0x86, 0xC9), Color.FromRgb(0x2E, 0x63, 0xA5), Color.FromRgb(0x2E, 0x63, 0xA5),
        Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    private static readonly ControlPalette PalLuna = new(
        Color.FromRgb(0x5A, 0x9B, 0xD6), Color.FromRgb(0x2A, 0x5E, 0x9E), Color.FromRgb(0x1E, 0x4E, 0x8C),
        Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    private static readonly ControlPalette PalWin98 = new(
        Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x80, 0x80, 0x80), Color.FromRgb(0x00, 0x00, 0x00),
        Color.FromRgb(0x00, 0x00, 0x00));
    private static readonly ControlPalette PalMetro = new(
        Color.FromRgb(0x4C, 0x8D, 0xFF), Color.FromRgb(0x4C, 0x8D, 0xFF), Color.FromRgb(0x4C, 0x8D, 0xFF),
        Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

    public static readonly IReadOnlyDictionary<WindowTheme, ThemeRule> Rules =
        new Dictionary<WindowTheme, ThemeRule>
        {
            [WindowTheme.None]  = new("无主题",            null,                                   null,                        null, false, PalNone),
            [WindowTheme.Mica]  = new("云母（Mica）",      null, Color.FromRgb(0x2A, 0x2D, 0x33),  false, false, PalMica),
            [WindowTheme.Aero]  = new("Aero",              null, Color.FromRgb(0x21, 0x2B, 0x38),  false, false, PalAero),
            [WindowTheme.Luna]  = new("WinXP Luna",        Color.FromRgb(0x2E, 0x6B, 0xD6),        Color.FromRgb(0xEC, 0xE9, 0xD8), true,  true,  PalLuna),
            [WindowTheme.Win98] = new("Win98 Classic",     Color.FromRgb(0x5A, 0x5A, 0x5A),        Color.FromRgb(0xC0, 0xC0, 0xC0), true,  true,  PalWin98),
            [WindowTheme.Metro] = new("Metro",             null, Color.FromRgb(0x1B, 0x1B, 0x1B),  false, false, PalMetro)
        };

    /// <summary>主题默认配色（选择主题时填入色板作为起点，之后仍可自行修改）。
    /// 无主题 = 应用默认观感（默认蓝 + 深面板 + 夜晚）。</summary>
    public static (Color? Bar, Color? Panel, bool? Day) Defaults(WindowTheme theme)
    {
        var r = Rules.TryGetValue(theme, out var rule) ? rule : Rules[WindowTheme.None];
        if (theme == WindowTheme.None)
            return (Color.FromRgb(0x4C, 0x8D, 0xFF), Color.FromRgb(0x14, 0x1A, 0x24), false);
        return (r.BarDefault, r.PanelDefault, r.DayDefault);
    }

    /// <summary>主题的控件（按键）风格色板。</summary>
    public static ControlPalette Controls(WindowTheme theme)
        => (Rules.TryGetValue(theme, out var rule) ? rule : Rules[WindowTheme.None]).Controls;

    /// <summary>解析最终配色：(窄条色, 面板色, 是否白天)。颜色一律取用户自定义值；白天=面板提亮、黑夜=浅色主题自动压暗。</summary>
    public static (Color Bar, Color Panel, bool Day) Resolve(SidebarConfig cfg)
    {
        Color bar = ColorFromHex(cfg.AccentColor, Color.FromRgb(0x4C, 0x8D, 0xFF));
        Color panel = ColorFromHex(cfg.PanelColor, Color.FromRgb(0x14, 0x1A, 0x24));
        bool day = cfg.DayNight == DayNight.Day;

        if (day)
        {
            panel = Lighten(panel, 0.78f); // 白天：浅色面板
        }
        else if (Rules.TryGetValue(cfg.WindowTheme, out var rule) && rule.IsLightScheme)
        {
            panel = Darken(panel, 0.55f); // 黑夜 + 浅色复古主题（Luna/Win98）：压暗避免晃眼
        }
        return (bar, panel, day);
    }

    private static Color ColorFromHex(string? hex, Color fallback)
        => hex != null && ColorConverter.ConvertFromString(hex) is Color c ? c : fallback;

    private static Color Lighten(Color c, float t) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * t),
        (byte)(c.G + (255 - c.G) * t),
        (byte)(c.B + (255 - c.B) * t));

    private static Color Darken(Color c, float t) => Color.FromRgb(
        (byte)(c.R * (1 - t)),
        (byte)(c.G * (1 - t)),
        (byte)(c.B * (1 - t)));
}
