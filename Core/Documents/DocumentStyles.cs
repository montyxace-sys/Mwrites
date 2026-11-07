using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Mwrites.Core.Documents;

public static class DocumentStyles
{
    public const string Normal = "Normal";
    public const string Title = "Title";
    public const string Subtitle = "Subtitle";
    public const string Heading1 = "Heading1";
    public const string Heading2 = "Heading2";
    public const string Heading3 = "Heading3";
    public const string Quote = "Quote";

    public static readonly string[] All = { Normal, Title, Subtitle, Heading1, Heading2, Heading3, Quote };

    private static readonly FontFamily BodyFont = new("Calibri");
    private static readonly FontFamily DisplayFont = new("Calibri Light");

    private static readonly Brush HeadingBrush = Frozen("#2E74B5");
    private static readonly Brush TitleBrush = Frozen("#1F3864");
    private static readonly Brush MutedBrush = Frozen("#595959");

    public static double Pt(double points) => points * 96.0 / 72.0;

    public static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static FlowDocument CreateDocument(PageSetup setup)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = BodyFont,
            FontSize = Pt(11),
            Foreground = Frozen("#000000"),
            LineHeight = double.NaN,
            PageWidth = setup.ContentWidth,
            Tag = setup
        };
        ApplyStyles(document);
        return document;
    }

    public static void ApplyStyles(FlowDocument document)
    {
        foreach (var key in All)
        {
            document.Resources[key] = Build(key);
        }
    }

    public static Style GetStyle(FlowDocument document, string key) =>
        document.Resources[key] as Style ?? Build(Normal);

    public static string DisplayName(string key) => key switch
    {
        Title => "Título",
        Subtitle => "Subtítulo",
        Heading1 => "Título 1",
        Heading2 => "Título 2",
        Heading3 => "Título 3",
        Quote => "Cita",
        _ => "Normal"
    };

    public static string StyleKeyFromName(string styleName) => styleName switch
    {
        "Título" => Title,
        "Subtítulo" => Subtitle,
        "Título 1" => Heading1,
        "Título 2" => Heading2,
        "Título 3" => Heading3,
        "Cita" => Quote,
        _ => Normal
    };

    public static int HeadingLevel(string key) => key switch
    {
        Title => 0,
        Heading1 => 1,
        Heading2 => 2,
        Heading3 => 3,
        _ => -1
    };

    public static bool IsHeading(string key) => HeadingLevel(key) >= 0;

    public static Paragraph CreateParagraph(FlowDocument document, string key = Normal, string? text = null)
    {
        var paragraph = new Paragraph();
        if (text is not null) paragraph.Inlines.Add(new Run(text));
        ApplyStyle(document, paragraph, key);
        return paragraph;
    }

    public static void ApplyStyle(FlowDocument document, Paragraph paragraph, string key)
    {
        paragraph.Style = GetStyle(document, key);
        paragraph.Tag = key;
    }

    public static string StyleKeyOf(Paragraph paragraph) =>
        paragraph.Tag as string ?? Normal;

    public static void RefreshStyles(FlowDocument document)
    {
        var paragraphs = new List<Paragraph>();
        CollectParagraphs(document.Blocks, paragraphs);
        ApplyStyles(document);
        foreach (var paragraph in paragraphs)
        {
            ApplyStyle(document, paragraph, StyleKeyOf(paragraph));
        }
    }

    private static void CollectParagraphs(BlockCollection blocks, List<Paragraph> paragraphs)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    paragraphs.Add(paragraph);
                    break;
                case List list:
                    foreach (var item in list.ListItems) CollectParagraphs(item.Blocks, paragraphs);
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                    foreach (var cell in row.Cells)
                        CollectParagraphs(cell.Blocks, paragraphs);
                    break;
                case Section section:
                    CollectParagraphs(section.Blocks, paragraphs);
                    break;
            }
        }
    }

    private static Style Build(string key)
    {
        var style = new Style(typeof(Paragraph));
        switch (key)
        {
            case Title:
                style.Setters.Add(new Setter(TextElement.FontFamilyProperty, DisplayFont));
                style.Setters.Add(new Setter(TextElement.FontSizeProperty, Pt(28)));
                style.Setters.Add(new Setter(TextElement.ForegroundProperty, TitleBrush));
                style.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0, 0, 0, Pt(6))));
                break;
            case Subtitle:
                style.Setters.Add(new Setter(TextElement.FontFamilyProperty, DisplayFont));
                style.Setters.Add(new Setter(TextElement.FontSizeProperty, Pt(14)));
                style.Setters.Add(new Setter(TextElement.FontStyleProperty, FontStyles.Italic));
                style.Setters.Add(new Setter(TextElement.ForegroundProperty, MutedBrush));
                style.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0, 0, 0, Pt(14))));
                break;
            case Heading1:
                style.Setters.Add(new Setter(TextElement.FontSizeProperty, Pt(16)));
                style.Setters.Add(new Setter(TextElement.FontWeightProperty, FontWeights.Bold));
                style.Setters.Add(new Setter(TextElement.ForegroundProperty, HeadingBrush));
                style.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0, Pt(16), 0, Pt(6))));
                style.Setters.Add(new Setter(Paragraph.KeepWithNextProperty, true));
                break;
            case Heading2:
                style.Setters.Add(new Setter(TextElement.FontSizeProperty, Pt(13)));
                style.Setters.Add(new Setter(TextElement.FontWeightProperty, FontWeights.Bold));
                style.Setters.Add(new Setter(TextElement.ForegroundProperty, HeadingBrush));
                style.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0, Pt(12), 0, Pt(4))));
                style.Setters.Add(new Setter(Paragraph.KeepWithNextProperty, true));
                break;
            case Heading3:
                style.Setters.Add(new Setter(TextElement.FontSizeProperty, Pt(12)));
                style.Setters.Add(new Setter(TextElement.FontWeightProperty, FontWeights.Bold));
                style.Setters.Add(new Setter(TextElement.ForegroundProperty, Frozen("#1F3864")));
                style.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0, Pt(10), 0, Pt(3))));
                style.Setters.Add(new Setter(Paragraph.KeepWithNextProperty, true));
                break;
            case Quote:
                style.Setters.Add(new Setter(TextElement.FontStyleProperty, FontStyles.Italic));
                style.Setters.Add(new Setter(TextElement.ForegroundProperty, MutedBrush));
                style.Setters.Add(new Setter(Block.MarginProperty, new Thickness(Pt(18), Pt(6), 0, Pt(10))));
                style.Setters.Add(new Setter(Block.PaddingProperty, new Thickness(Pt(8), 0, 0, 0)));
                style.Setters.Add(new Setter(Block.BorderBrushProperty, Frozen("#9CC3E5")));
                style.Setters.Add(new Setter(Block.BorderThicknessProperty, new Thickness(3, 0, 0, 0)));
                break;
            default:
                style.Setters.Add(new Setter(TextElement.FontFamilyProperty, BodyFont));
                style.Setters.Add(new Setter(TextElement.FontSizeProperty, Pt(11)));
                style.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
                break;
        }
        style.Seal();
        return style;
    }
}
