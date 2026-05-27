using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Stranichnik.Views;

public sealed partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
        Opened += (_, _) => OkButton.Focus();
    }

    private MessageDialog(string title, string message)
        : this()
    {
        Title = title;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
    }

    public static Task<object?> ShowError(Window owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message);

        return dialog.ShowDialog<object?>(owner);
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }
}
