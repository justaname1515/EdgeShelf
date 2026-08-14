using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EdgeShelf;

/// <summary>true → Collapsed，false → Visible（与 BooleanToVisibilityConverter 相反）。</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
