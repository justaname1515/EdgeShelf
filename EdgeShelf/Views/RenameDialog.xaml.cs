using System.Windows;
using System.Windows.Input;

namespace EdgeShelf.Views;

public partial class RenameDialog : Window
{
    public string? Result { get; private set; }

    public RenameDialog(string title, string prompt, string initialText)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        NameBox.Text = initialText;
        NameBox.SelectAll();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(Result)) return; // 不允许空名
        DialogResult = true;
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Ok_Click(sender, e);
    }
}
