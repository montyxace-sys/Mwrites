using System.Windows;

namespace Mwrites.Dialogs;

public partial class InsertTableDialog : Window
{
    public int Rows { get; private set; } = 3;
    public int Columns { get; private set; } = 3;

    public InsertTableDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => RowsBox.SelectAll();
        RowsBox.Focus();
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RowsBox.Text, out var rows) || rows < 1 || rows > 30)
        {
            MessageBox.Show(this, "Indica un número de filas entre 1 y 30.", "Mwrites",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!int.TryParse(ColumnsBox.Text, out var columns) || columns < 1 || columns > 12)
        {
            MessageBox.Show(this, "Indica un número de columnas entre 1 y 12.", "Mwrites",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Rows = rows;
        Columns = columns;
        DialogResult = true;
    }
}
