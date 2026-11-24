namespace Mwrites.Portable.Core;

public enum PortableStyle
{
    Normal,
    Heading1,
    Heading2,
    Heading3,
    Subtitle,
    Quote
}

public enum PortableAlignment
{
    Left,
    Center,
    Right,
    Justify
}

public enum PortableListKind
{
    None,
    Bullet,
    Numbered
}

/// <summary>Un fragmento de texto con su formato. El editor portable lo aplana al mostrarlo.</summary>
public sealed class PortableRun
{
    public string Text { get; set; } = string.Empty;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string? FontFamily { get; set; }
    public double? FontSizePt { get; set; }
    public string? ColorHex { get; set; }
}

public sealed class PortableParagraph
{
    public PortableStyle Style { get; set; } = PortableStyle.Normal;
    public PortableAlignment Alignment { get; set; } = PortableAlignment.Left;
    public PortableListKind List { get; set; } = PortableListKind.None;
    public List<PortableRun> Runs { get; set; } = new();

    public string Text => string.Concat(Runs.Select(run => run.Text));

    public static PortableParagraph Plain(string text, PortableStyle style = PortableStyle.Normal) =>
        new() { Style = style, Runs = new List<PortableRun> { new() { Text = text } } };
}

public sealed class PortablePageSetup
{
    public string SizeName { get; set; } = "A4";
    public double WidthPt { get; set; } = 595.3;
    public double HeightPt { get; set; } = 841.9;
    public bool Landscape { get; set; }
    public double MarginMm { get; set; } = 25.4;

    public static PortablePageSetup A4 => new();

    public static PortablePageSetup Letter => new()
    {
        SizeName = "Carta",
        WidthPt = 612,
        HeightPt = 792,
        MarginMm = 25.4
    };
}

public sealed class PortableDocument
{
    public PortablePageSetup Setup { get; set; } = new();
    public List<PortableParagraph> Paragraphs { get; set; } = new();

    /// <summary>Texto sin marcas de estilo, tal como se guarda en un .txt.</summary>
    public string PlainText => string.Join("\n", Paragraphs.Select(p => p.Text));

    /// <summary>Texto con marcas de estilo, tal como se muestra en el editor portable.</summary>
    public string EditorText => string.Join("\n", Paragraphs.Select(PortableMarkers.Format));

    public static PortableDocument Sample()
    {
        var document = new PortableDocument();
        document.Paragraphs.Add(PortableParagraph.Plain("Documento de ejemplo", PortableStyle.Heading1));
        document.Paragraphs.Add(PortableParagraph.Plain(
            "Mwrites portable abre y guarda documentos DOCX, TXT y MWDOC sin conexión."));
        document.Paragraphs.Add(PortableParagraph.Plain("Novedades de la versión 1.1", PortableStyle.Heading2));
        document.Paragraphs.Add(PortableParagraph.Plain("Cambiar mayúsculas y minúsculas de la selección."));
        document.Paragraphs.Add(new PortableParagraph
        {
            List = PortableListKind.Bullet,
            Runs = new List<PortableRun> { new() { Text = "Lista con viñetas" } }
        });
        document.Paragraphs.Add(PortableParagraph.Plain(
            "Las notas entre comillas usan el estilo Cita.", PortableStyle.Quote));
        return document;
    }
}

/// <summary>Marcas sencillas para representar estilos en texto plano y en el editor.</summary>
public static class PortableMarkers
{
    public const string Heading1 = "# ";
    public const string Heading2 = "## ";
    public const string Heading3 = "### ";
    public const string Quote = "> ";
    public const string Bullet = "- ";
    public const string Numbered = "1. ";

    public static string Format(PortableParagraph paragraph)
    {
        var prefix = paragraph.List switch
        {
            PortableListKind.Bullet => Bullet,
            PortableListKind.Numbered => Numbered,
            _ => paragraph.Style switch
            {
                PortableStyle.Heading1 => Heading1,
                PortableStyle.Heading2 => Heading2,
                PortableStyle.Heading3 => Heading3,
                PortableStyle.Quote => Quote,
                _ => string.Empty
            }
        };
        return prefix + paragraph.Text;
    }

    public static PortableParagraph ParseLine(string line)
    {
        if (line.StartsWith(Heading3, StringComparison.Ordinal))
            return PortableParagraph.Plain(line[Heading3.Length..], PortableStyle.Heading3);
        if (line.StartsWith(Heading2, StringComparison.Ordinal))
            return PortableParagraph.Plain(line[Heading2.Length..], PortableStyle.Heading2);
        if (line.StartsWith(Heading1, StringComparison.Ordinal))
            return PortableParagraph.Plain(line[Heading1.Length..], PortableStyle.Heading1);
        if (line.StartsWith(Quote, StringComparison.Ordinal))
            return PortableParagraph.Plain(line[Quote.Length..], PortableStyle.Quote);
        if (line.StartsWith(Bullet, StringComparison.Ordinal))
            return new PortableParagraph { List = PortableListKind.Bullet,
                Runs = new List<PortableRun> { new() { Text = line[Bullet.Length..] } } };
        if (line.StartsWith(Numbered, StringComparison.Ordinal))
            return new PortableParagraph { List = PortableListKind.Numbered,
                Runs = new List<PortableRun> { new() { Text = line[Numbered.Length..] } } };
        return PortableParagraph.Plain(line);
    }
}
