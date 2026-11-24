using Mwrites.Core.Documents;

namespace Mwrites.Portable.Core;

/// <summary>Búsqueda y reemplazo sobre el documento portable.</summary>
public static class PortableFind
{
    public static List<(int Paragraph, int Start, int Length)> FindAll(
        PortableDocument document, string query, bool matchCase = false, bool wholeWord = false)
    {
        var results = new List<(int, int, int)>();
        if (string.IsNullOrEmpty(query)) return results;

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var index = 0; index < document.Paragraphs.Count; index++)
        {
            var text = document.Paragraphs[index].Text;
            var position = 0;
            while (position <= text.Length - query.Length)
            {
                var found = text.IndexOf(query, position, comparison);
                if (found < 0) break;
                var end = found + query.Length;
                if (!wholeWord || IsWholeWord(text, found, end)) results.Add((index, found, query.Length));
                position = found + Math.Max(1, query.Length);
            }
        }
        return results;
    }

    public static int ReplaceAll(
        PortableDocument document, string query, string replacement, bool matchCase = false, bool wholeWord = false)
    {
        var matches = FindAll(document, query, matchCase, wholeWord);
        if (matches.Count == 0) return 0;

        foreach (var group in matches.GroupBy(match => match.Paragraph))
        {
            var paragraph = document.Paragraphs[group.Key];
            var text = paragraph.Text;
            var template = paragraph.Runs.FirstOrDefault();
            foreach (var match in group.OrderByDescending(item => item.Start))
            {
                text = text.Remove(match.Start, match.Length).Insert(match.Start, replacement);
            }
            paragraph.Runs = new List<PortableRun>
            {
                new()
                {
                    Text = text,
                    Bold = template?.Bold ?? false,
                    Italic = template?.Italic ?? false,
                    Underline = template?.Underline ?? false,
                    FontFamily = template?.FontFamily,
                    FontSizePt = template?.FontSizePt,
                    ColorHex = template?.ColorHex
                }
            };
        }
        return matches.Count;
    }

    public static void ApplyCase(PortableDocument document, TextCase mode, int paragraphIndex = -1,
        int start = -1, int length = -1)
    {
        for (var index = 0; index < document.Paragraphs.Count; index++)
        {
            if (paragraphIndex >= 0 && index != paragraphIndex) continue;
            var paragraph = document.Paragraphs[index];
            var text = paragraph.Text;
            if (paragraphIndex >= 0 && start >= 0 && start + length <= text.Length)
            {
                var part = TextTransforms.Apply(text.Substring(start, length), mode);
                text = text.Remove(start, length).Insert(start, part);
            }
            else
            {
                text = TextTransforms.Apply(text, mode);
            }
            var template = paragraph.Runs.FirstOrDefault();
            paragraph.Runs = new List<PortableRun>
            {
                new()
                {
                    Text = text,
                    Bold = template?.Bold ?? false,
                    Italic = template?.Italic ?? false,
                    Underline = template?.Underline ?? false,
                    FontFamily = template?.FontFamily,
                    FontSizePt = template?.FontSizePt,
                    ColorHex = template?.ColorHex
                }
            };
        }
    }

    private static bool IsWholeWord(string text, int start, int end)
    {
        var before = start > 0 && IsWordChar(text[start - 1]);
        var after = end < text.Length && IsWordChar(text[end]);
        return !before && !after;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
