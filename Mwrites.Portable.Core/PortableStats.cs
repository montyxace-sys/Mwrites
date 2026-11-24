using System.Text.RegularExpressions;

namespace Mwrites.Portable.Core;

public readonly record struct PortableStats(
    int Words, int Characters, int CharactersNoSpaces, int Paragraphs, int Headings)
{
    private static readonly Regex WordPattern =
        new(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*", RegexOptions.Compiled);

    public static PortableStats Compute(PortableDocument document)
    {
        var words = 0;
        var characters = 0;
        var charactersNoSpaces = 0;
        var headings = 0;

        foreach (var paragraph in document.Paragraphs)
        {
            var text = paragraph.Text;
            words += WordPattern.Matches(text).Count;
            characters += text.Length;
            charactersNoSpaces += text.Count(c => !char.IsWhiteSpace(c));
            if (paragraph.Style is PortableStyle.Heading1 or PortableStyle.Heading2 or PortableStyle.Heading3)
                headings++;
        }

        return new PortableStats(words, characters, charactersNoSpaces, document.Paragraphs.Count, headings);
    }
}
