using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using Mwrites.Core.Documents;

namespace Mwrites.Dialogs;

public partial class WordCountDialog : Window
{
    private static readonly Regex WordPattern = new(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*");

    public WordCountDialog(FlowDocument document, int pages, string selectionText)
    {
        InitializeComponent();
        var stats = DocumentStats.Compute(document);

        PagesValue.Text = pages.ToString();
        WordsValue.Text = stats.Words.ToString("N0");
        CharactersValue.Text = stats.Characters.ToString("N0");
        CharactersNoSpacesValue.Text = stats.CharactersNoSpaces.ToString("N0");
        ParagraphsValue.Text = stats.Paragraphs.ToString("N0");

        if (!string.IsNullOrWhiteSpace(selectionText))
        {
            SelectionCard.Visibility = Visibility.Visible;
            var words = WordPattern.Matches(selectionText).Count;
            var characters = selectionText.Length;
            var charactersNoSpaces = selectionText.Count(c => !char.IsWhiteSpace(c));
            SelectionSummary.Text = $"{words:N0} palabra(s) · {characters:N0} carácter(es) · {charactersNoSpaces:N0} sin espacios";
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
