using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mwrites.Core.Documents;

namespace Mwrites.Dialogs;

public partial class FindReplaceDialog : Window
{
    private readonly RichTextBox _editor;

    public FindReplaceDialog(RichTextBox editor, bool replaceMode)
    {
        InitializeComponent();
        _editor = editor;
        if (replaceMode)
        {
            ReplaceBox.Focus();
        }
        else
        {
            ReplaceBox.IsEnabled = false;
            Loaded += (_, _) => SearchBox.Focus();
        }
        SearchBox.KeyDown += OnSearchKeyDown;
    }

    private FindOptions Options => new(MatchCaseCheck.IsChecked == true, WholeWordCheck.IsChecked == true);

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnFindNext(sender, e);
            e.Handled = true;
        }
    }

    private void OnFindNext(object sender, RoutedEventArgs e)
    {
        var query = SearchBox.Text;
        if (string.IsNullOrEmpty(query)) return;

        var document = _editor.Document;
        var options = Options;
        var total = FindEngine.FindAll(document, query, options).Count;
        if (total == 0)
        {
            StatusText.Text = "No se han encontrado coincidencias.";
            return;
        }

        var from = _editor.Selection.IsEmpty ? _editor.CaretPosition : _editor.Selection.End;
        var match = FindEngine.FindNext(document, query, options, from);
        if (match is null)
        {
            StatusText.Text = "No hay más coincidencias.";
            return;
        }

        _editor.Selection.Select(match.Start, match.End);
        _editor.Focus();
        StatusText.Text = $"{total} coincidencia(s) en el documento.";
    }

    private void OnReplace(object sender, RoutedEventArgs e)
    {
        var query = SearchBox.Text;
        if (string.IsNullOrEmpty(query)) return;

        var selection = _editor.Selection;
        var selected = selection.Text;
        var comparison = MatchCaseCheck.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!selection.IsEmpty && string.Equals(selected, query, comparison))
        {
            selection.Text = ReplaceBox.Text;
        }
        OnFindNext(sender, e);
    }

    private void OnReplaceAll(object sender, RoutedEventArgs e)
    {
        var query = SearchBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            StatusText.Text = "Escribe el texto que quieres reemplazar.";
            return;
        }

        var count = FindEngine.ReplaceAll(_editor.Document, query, ReplaceBox.Text, Options);
        StatusText.Text = count == 0
            ? "No se han encontrado coincidencias."
            : $"Se han reemplazado {count} coincidencia(s).";
        _editor.Focus();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
