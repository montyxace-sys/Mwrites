using System.Windows;

namespace Mwrites.Dialogs;

public partial class SimpleInputDialog : Window
{
    public string Value => ValueBox.Text;

    public SimpleInputDialog(string title, string message, string initialValue)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ValueBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
