using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EdgeShelf.Models;

namespace EdgeShelf.Views;

/// <summary>新建侧边栏对话框：选择「单独建立」或「作为页签加入现有侧边栏」。</summary>
public class NewSidebarDialog : Window
{
    private readonly List<SidebarConfig> _sidebars;
    private readonly RadioButton _standalone;
    private readonly RadioButton _asTab;
    private readonly ComboBox _targetCombo;

    public bool AsTab => _asTab.IsChecked == true;
    public SidebarConfig? Target => AsTab && _targetCombo.SelectedIndex >= 0 && _targetCombo.SelectedIndex < _sidebars.Count
        ? _sidebars[_targetCombo.SelectedIndex]
        : null;

    public NewSidebarDialog(List<SidebarConfig> sidebars)
    {
        _sidebars = sidebars;
        Title = "新建侧边栏";
        Width = 390;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.None;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x2E));
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/App.ico"));
            if (info != null) { using var s = info.Stream; Icon = System.Windows.Media.Imaging.BitmapFrame.Create(s); }
        }
        catch { }

        var titleBar = new Grid();
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.Children.Add(new TextBlock
        {
            Text = "新建侧边栏",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        });
        var closeBtn = new Button
        {
            Content = "\uE8BB",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 11,
            Width = 26,
            Height = 24,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand
        };
        closeBtn.Click += (_, _) => Close();
        Grid.SetColumn(closeBtn, 1);
        titleBar.Children.Add(closeBtn);

        _standalone = new RadioButton
        {
            Content = "单独建立一个新的独立侧边栏",
            Foreground = LightText(),
            FontSize = 12.5,
            IsChecked = true,
            GroupName = "ns",
            Margin = new Thickness(0, 10, 0, 0)
        };
        _asTab = new RadioButton
        {
            Content = "作为页签加入现有侧边栏",
            Foreground = LightText(),
            FontSize = 12.5,
            GroupName = "ns",
            Margin = new Thickness(0, 6, 0, 0)
        };
        _asTab.Checked += (_, _) => UpdateTargetEnabled();

        _targetCombo = new ComboBox { Width = 190, FontSize = 12, Margin = new Thickness(8, 0, 0, 0) };
        _targetCombo.ItemsSource = _sidebars.Select(s => s.Name).ToList();
        if (_sidebars.Count > 0) _targetCombo.SelectedIndex = 0;

        var targetRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 8, 0, 0) };
        targetRow.Children.Add(new TextBlock { Text = "目标侧边栏", Foreground = SubText(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        targetRow.Children.Add(_targetCombo);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button
        {
            Content = "取消",
            Width = 76,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0),
            IsCancel = true,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3B)),
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1)
        };
        var ok = new Button
        {
            Content = "新建",
            Width = 76,
            Height = 30,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        root.Children.Add(titleBar);
        root.Children.Add(_standalone);
        root.Children.Add(_asTab);
        root.Children.Add(targetRow);
        root.Children.Add(buttons);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x2E)),
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Child = root
        };
        UpdateTargetEnabled();
    }

    private void UpdateTargetEnabled()
    {
        bool asTab = _asTab.IsChecked == true;
        _targetCombo.IsEnabled = asTab;
        if (!asTab) _targetCombo.SelectedIndex = -1;
        else if (_targetCombo.SelectedIndex < 0 && _sidebars.Count > 0) _targetCombo.SelectedIndex = 0;
    }

    private static Brush LightText() => new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
    private static Brush SubText() => new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));
}
