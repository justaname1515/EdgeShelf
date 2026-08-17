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
        bool IsLightScheme);    // 浅色复古主题：黑夜下自动压暗，避免晃眼

    public static readonly IReadOnlyDictionary<WindowTheme, ThemeRule> Rules =
        new Dictionary<WindowTheme, ThemeRule>
        {
            [WindowTheme.None]  = new("无主题",            null,                                   null,                        null, false),
            [WindowTheme.Mica]  = new("云母（Mica）",      null, Color.FromRgb(0x2A, 0x2D, 0x33),  false, false),
            [WindowTheme.Aero]  = new("Aero",              null, Color.FromRgb(0x21, 0x2B, 0x38),  false, false),
            [WindowTheme.Luna]  = new("WinXP Luna",        Color.FromRgb(0x2E, 0x6B, 0xD6),        Color.FromRgb(0xEC, 0xE9, 0xD8), true,  true),
            [WindowTheme.Win98] = new("Win98 Classic",     Color.FromRgb(0x5A, 0x5A, 0x5A),        Color.FromRgb(0xC0, 0xC0, 0xC0), true,  true),
            [WindowTheme.Metro] = new("Metro",             null, Color.FromRgb(0x1B, 0x1B, 0x1B),  false, false)
        };

    /// <summary>主题默认配色（选择主题时填入色板作为起点，之后仍可自行修改）。</summary>
    public static (Color? Bar, Color? Panel, bool? Day) Defaults(WindowTheme theme)
    {
        var r = Rules.TryGetValue(theme, out var rule) ? rule : Rules[WindowTheme.None];
        return (r.BarDefault, r.PanelDefault, r.DayDefault);
    }

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
