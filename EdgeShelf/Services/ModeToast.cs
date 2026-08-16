using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EdgeShelf.Services;

/// <summary>模式切换的可见反馈：屏幕边缘弹出小气泡，1.2 秒后淡出，点击穿透不挡操作。</summary>
public static class ModeToast
{
    private static Window? _current;

    public static void Show(string text)
    {
        try
        {
            _current?.Close();
            var w = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Width = 150,
                Height = 36,
                IsHitTestVisible = false
            };
            w.SourceInitialized += (_, _) =>
            {
                // 点击穿透
                try
                {
                    var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                    int ex = GetWindowLong(h, GWL_EXSTYLE);
                    SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                }
                catch { }
            };
            w.Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(225, 24, 28, 36)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            // 定位：屏幕右上角工作区内侧
            var wa = SystemParameters.WorkArea;
            w.Left = wa.Right - w.Width - 24;
            w.Top = wa.Top + 48;
            w.Show();
            _current = w;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
                fade.Completed += (_, _) => w.Close();
                w.BeginAnimation(UIElement.OpacityProperty, fade);
            };
            timer.Start();
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
