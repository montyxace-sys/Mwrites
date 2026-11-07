using System.Windows.Documents;

namespace Mwrites.Core.Documents;

public sealed record FindOptions(bool MatchCase = false, bool WholeWord = false);

public static class FindEngine
{
    public static List<TextRange> FindAll(FlowDocument document, string query, FindOptions options)
    {
        var results = new List<TextRange>();
        if (string.IsNullOrEmpty(query)) return results;

        var (text, map) = BuildText(document);
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var index = 0;
        while (index <= text.Length - query.Length)
        {
            var found = text.IndexOf(query, index, comparison);
            if (found < 0) break;

            var end = found + query.Length;
            if (IsUsable(map, found, end) && (!options.WholeWord || IsWholeWord(text, found, end)))
            {
                results.Add(new TextRange(map[found]!, map[end - 1]!.GetPositionAtOffset(1, LogicalDirection.Forward)!));
            }
            index = found + Math.Max(1, query.Length);
        }
        return results;
    }

    public static TextRange? FindNext(FlowDocument document, string query, FindOptions options, TextPointer? from)
    {
        var matches = FindAll(document, query, options);
        if (matches.Count == 0) return null;
        if (from is null) return matches[0];
        foreach (var match in matches)
        {
            if (match.Start.CompareTo(from) >= 0) return match;
        }
        return matches[0];
    }

    public static int ReplaceAll(FlowDocument document, string query, string replacement, FindOptions options)
    {
        var matches = FindAll(document, query, options);
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            matches[i].Text = replacement;
        }
        return matches.Count;
    }

    public static bool ReplaceCurrent(TextRange range, string replacement)
    {
        range.Text = replacement;
        return true;
    }

    private static bool IsUsable(List<TextPointer?> map, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (i >= map.Count || map[i] is null) return false;
        }
        return true;
    }

    private static bool IsWholeWord(string text, int start, int end)
    {
        var before = start > 0 && IsWordChar(text[start - 1]);
        var after = end < text.Length && IsWordChar(text[end]);
        return !before && !after;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static (string Text, List<TextPointer?> Map) BuildText(FlowDocument document)
    {
        var builder = new System.Text.StringBuilder();
        var map = new List<TextPointer?>();
        AppendBlocks(document.Blocks, builder, map);
        return (builder.ToString(), map);
    }

    private static void AppendBlocks(BlockCollection blocks, System.Text.StringBuilder builder, List<TextPointer?> map)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    AppendInlines(paragraph.Inlines, builder, map);
                    builder.Append('\n');
                    map.Add(null);
                    break;
                case List list:
                    foreach (var item in list.ListItems) AppendBlocks(item.Blocks, builder, map);
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                    foreach (var cell in row.Cells)
                        AppendBlocks(cell.Blocks, builder, map);
                    break;
                case Section section:
                    AppendBlocks(section.Blocks, builder, map);
                    break;
            }
        }
    }

    private static void AppendInlines(InlineCollection inlines, System.Text.StringBuilder builder, List<TextPointer?> map)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    AppendRun(run, builder, map);
                    break;
                case LineBreak lineBreak:
                    builder.Append('\n');
                    map.Add(lineBreak.ContentStart);
                    break;
                case Span span:
                    AppendInlines(span.Inlines, builder, map);
                    break;
                case Figure figure:
                    AppendBlocks(figure.Blocks, builder, map);
                    break;
                case Floater floater:
                    AppendBlocks(floater.Blocks, builder, map);
                    break;
            }
        }
    }

    private static void AppendRun(Run run, System.Text.StringBuilder builder, List<TextPointer?> map)
    {
        // Run.Text avoids the list marker that TextRange reports for list paragraphs.
        var text = run.Text;
        if (text.Length == 0) return;

        var pointer = run.ContentStart;
        for (var i = 0; i < text.Length; i++)
        {
            builder.Append(text[i]);
            map.Add(pointer);
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(text[i + 1]);
                map.Add(pointer);
                i++;
            }
            pointer = pointer.GetPositionAtOffset(1, LogicalDirection.Forward);
            if (pointer is null) break;
        }
    }
}
