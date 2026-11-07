using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace Mwrites.Core.Documents;

public readonly record struct DocumentStats(int Words, int Characters, int CharactersNoSpaces, int Paragraphs, int Headings)
{
    private static readonly Regex WordPattern = new(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*", RegexOptions.Compiled);

    public static DocumentStats Compute(FlowDocument document)
    {
        var paragraphs = new List<Paragraph>();
        Collect(document.Blocks, paragraphs);
        var words = 0;
        var characters = 0;
        var charactersNoSpaces = 0;
        var headings = 0;

        foreach (var paragraph in paragraphs)
        {
            var text = TextOf(paragraph);
            words += WordPattern.Matches(text).Count;
            characters += text.Length;
            charactersNoSpaces += text.Count(c => !char.IsWhiteSpace(c));
            if (DocumentStyles.IsHeading(DocumentStyles.StyleKeyOf(paragraph))) headings++;
        }

        return new DocumentStats(words, characters, charactersNoSpaces, paragraphs.Count, headings);
    }

    public static string GetPlainText(FlowDocument document)
    {
        var paragraphs = new List<Paragraph>();
        Collect(document.Blocks, paragraphs);
        return string.Join(Environment.NewLine, paragraphs.Select(TextOf));
    }

    // TextRange over a list paragraph includes the bullet/number marker; walking the inlines
    // returns only the real content.
    public static string TextOf(Paragraph paragraph)
    {
        var builder = new StringBuilder();
        AppendInlines(paragraph.Inlines, builder);
        return builder.ToString();
    }

    private static void AppendInlines(InlineCollection inlines, StringBuilder builder)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    builder.Append(run.Text);
                    break;
                case LineBreak:
                    builder.Append('\n');
                    break;
                case Span span:
                    AppendInlines(span.Inlines, builder);
                    break;
                case Figure figure:
                    AppendBlocks(figure.Blocks, builder);
                    break;
                case Floater floater:
                    AppendBlocks(floater.Blocks, builder);
                    break;
            }
        }
    }

    private static void AppendBlocks(BlockCollection blocks, StringBuilder builder)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    AppendInlines(paragraph.Inlines, builder);
                    break;
                case List list:
                    foreach (var item in list.ListItems) AppendBlocks(item.Blocks, builder);
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                    foreach (var cell in row.Cells)
                        AppendBlocks(cell.Blocks, builder);
                    break;
                case Section section:
                    AppendBlocks(section.Blocks, builder);
                    break;
            }
        }
    }

    public static void Collect(BlockCollection blocks, List<Paragraph> paragraphs)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    paragraphs.Add(paragraph);
                    break;
                case List list:
                    foreach (var item in list.ListItems) Collect(item.Blocks, paragraphs);
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                    foreach (var cell in row.Cells)
                        Collect(cell.Blocks, paragraphs);
                    break;
                case Section section:
                    Collect(section.Blocks, paragraphs);
                    break;
            }
        }
    }
}
