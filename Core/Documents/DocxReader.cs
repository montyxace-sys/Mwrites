using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Mwrites.Core.Documents;

public static class DocxReader
{
    private static readonly XNamespace W = DocxNames.W;
    private static readonly XNamespace R = DocxNames.R;
    private static readonly XNamespace Wp = DocxNames.Wp;
    private static readonly XNamespace A = DocxNames.A;

    private sealed class ReadContext
    {
        public required ZipArchive Archive { get; init; }
        public required Dictionary<string, string> Relationships { get; init; }
        public required Dictionary<int, int> NumToAbstract { get; init; }
        public required Dictionary<int, Dictionary<int, string>> AbstractFormats { get; init; }
        public required Dictionary<string, ImportedStyle> Styles { get; init; }
        public required FlowDocument Document { get; init; }
        public Dictionary<string, (BitmapImage Bitmap, byte[] Bytes)> ImageCache { get; } = new();
    }

    private sealed record ImportedStyle(string Key, RunFormat? Format, ParagraphFormat? Paragraph);

    private sealed record RunFormat(
        string? FontFamily = null,
        double? FontSize = null,
        bool Bold = false,
        bool Italic = false,
        bool Underline = false,
        bool Strike = false,
        Brush? Foreground = null,
        Brush? Highlight = null,
        BaselineAlignment? Baseline = null);

    private sealed record ParagraphFormat(
        TextAlignment? Alignment = null,
        double? IndentLeft = null,
        double? IndentRight = null,
        double? FirstLine = null,
        double? SpacingBefore = null,
        double? SpacingAfter = null,
        bool PageBreakBefore = false,
        Brush? Background = null);

    public static FlowDocument Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var documentEntry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("El archivo no es un documento de Word (.docx) válido.");

        XDocument documentXml;
        using (var xmlStream = documentEntry.Open())
        {
            documentXml = XDocument.Load(xmlStream);
        }
        var body = documentXml.Root?.Element(W + "body")
            ?? throw new InvalidDataException("El documento no contiene un cuerpo válido.");

        var relationships = ReadRelationships(archive);
        var styles = ReadStyles(archive);
        var (numToAbstract, abstractFormats) = ReadNumbering(archive);
        var setup = ReadPageSetup(body);
        var document = DocumentStyles.CreateDocument(setup);

        var context = new ReadContext
        {
            Archive = archive,
            Relationships = relationships,
            NumToAbstract = numToAbstract,
            AbstractFormats = abstractFormats,
            Styles = styles,
            Document = document
        };

        var listStack = new List<List>();
        var currentNumId = -1;
        ReadBlocks(body, document.Blocks, context, listStack, ref currentNumId);

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(DocumentStyles.CreateParagraph(document));
        }
        return document;
    }

    private static void ReadBlocks(XElement container, BlockCollection target, ReadContext context, List<List> listStack, ref int currentNumId)
    {
        foreach (var element in container.Elements())
        {
            var name = element.Name;
            if (name == W + "p")
            {
                var paragraph = ReadParagraph(element, context);
                var numPr = element.Element(W + "pPr")?.Element(W + "numPr");
                if (numPr is not null)
                {
                    var numId = GetInt(numPr.Element(W + "numId")?.Attribute(W + "val"), -1);
                    var level = GetInt(numPr.Element(W + "ilvl")?.Attribute(W + "val"), 0);
                    AppendToList(paragraph, numId, level, target, context, listStack, ref currentNumId);
                }
                else
                {
                    listStack.Clear();
                    currentNumId = -1;
                    target.Add(paragraph);
                }
            }
            else if (name == W + "tbl")
            {
                listStack.Clear();
                currentNumId = -1;
                target.Add(ReadTable(element, context));
            }
            else if (name == W + "sdt")
            {
                var content = element.Element(W + "sdtContent");
                if (content is not null) ReadBlocks(content, target, context, listStack, ref currentNumId);
            }
            else if (name == W + "sectPr" || name == W + "bookmarkStart" || name == W + "bookmarkEnd")
            {
                continue;
            }
        }
    }

    private static void AppendToList(Paragraph paragraph, int numId, int level, BlockCollection target,
        ReadContext context, List<List> stack, ref int currentNumId)
    {
        level = Math.Clamp(level, 0, 8);
        if (numId != currentNumId)
        {
            stack.Clear();
            currentNumId = numId;
            var root = new List { MarkerStyle = MarkerStyle(context, numId, 0) };
            target.Add(root);
            stack.Add(root);
        }

        while (stack.Count < level + 1)
        {
            var parent = stack[^1];
            var lastItem = parent.ListItems.LastOrDefault();
            if (lastItem is null)
            {
                lastItem = new ListItem();
                parent.ListItems.Add(lastItem);
            }
            var nested = lastItem.Blocks.OfType<List>().LastOrDefault();
            if (nested is null)
            {
                nested = new List { MarkerStyle = MarkerStyle(context, numId, stack.Count) };
                lastItem.Blocks.Add(nested);
            }
            stack.Add(nested);
        }
        while (stack.Count > level + 1)
        {
            stack.RemoveAt(stack.Count - 1);
        }

        var list = stack[level];
        list.ListItems.Add(new ListItem(paragraph));
    }

    private static TextMarkerStyle MarkerStyle(ReadContext context, int numId, int level)
    {
        if (!context.NumToAbstract.TryGetValue(numId, out var abstractId)) return TextMarkerStyle.Disc;
        if (!context.AbstractFormats.TryGetValue(abstractId, out var levels)) return TextMarkerStyle.Disc;
        if (!levels.TryGetValue(level, out var format)) return TextMarkerStyle.Disc;
        return format switch
        {
            "bullet" => TextMarkerStyle.Disc,
            "decimal" => TextMarkerStyle.Decimal,
            "lowerLetter" => TextMarkerStyle.LowerLatin,
            "upperLetter" => TextMarkerStyle.UpperLatin,
            "lowerRoman" => TextMarkerStyle.LowerRoman,
            "upperRoman" => TextMarkerStyle.UpperRoman,
            _ => TextMarkerStyle.Decimal
        };
    }

    private static Paragraph ReadParagraph(XElement element, ReadContext context)
    {
        var paragraph = new Paragraph();
        var properties = element.Element(W + "pPr");
        var styleId = properties?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
        var imported = styleId is not null && context.Styles.TryGetValue(styleId, out var style) ? style : null;
        var key = imported?.Key ?? DocumentStyles.Normal;
        DocumentStyles.ApplyStyle(context.Document, paragraph, key);

        var paragraphFormat = ReadParagraphFormat(properties);
        if (imported?.Paragraph is { } styleParagraph)
        {
            paragraphFormat = MergeParagraphFormat(styleParagraph, paragraphFormat);
        }
        if (imported?.Format is { } styleRun)
        {
            ApplyRunFormatToParagraph(paragraph, styleRun);
        }
        ApplyParagraphFormat(paragraph, paragraphFormat);

        AppendInlines(element, paragraph, context);
        return paragraph;
    }

    private static ParagraphFormat? ReadParagraphFormat(XElement? properties)
    {
        if (properties is null) return null;
        var alignment = properties.Element(W + "jc")?.Attribute(W + "val")?.Value switch
        {
            "center" => TextAlignment.Center,
            "right" or "end" => TextAlignment.Right,
            "both" => TextAlignment.Justify,
            "left" or "start" => TextAlignment.Left,
            _ => (TextAlignment?)null
        };

        double? left = null, right = null, firstLine = null;
        var indent = properties.Element(W + "ind");
        if (indent is not null)
        {
            left = Twips(indent.Attribute(W + "left"));
            right = Twips(indent.Attribute(W + "right"));
            var first = Twips(indent.Attribute(W + "firstLine"));
            if (first is not null) firstLine = first;
            var hanging = Twips(indent.Attribute(W + "hanging"));
            if (hanging is not null) firstLine = -hanging;
        }

        double? before = null, after = null;
        var spacing = properties.Element(W + "spacing");
        if (spacing is not null)
        {
            before = Twips(spacing.Attribute(W + "before"));
            after = Twips(spacing.Attribute(W + "after"));
        }

        var background = properties.Element(W + "shd")?.Attribute(W + "fill")?.Value;
        Brush? shading = background is not null && background != "auto" ? BrushFromHex(background) : null;

        return new ParagraphFormat(alignment, left, right, firstLine, before, after,
            properties.Element(W + "pageBreakBefore") is not null, shading);
    }

    private static ParagraphFormat MergeParagraphFormat(ParagraphFormat style, ParagraphFormat? direct)
    {
        if (direct is null) return style;
        return new ParagraphFormat(
            direct.Alignment ?? style.Alignment,
            direct.IndentLeft ?? style.IndentLeft,
            direct.IndentRight ?? style.IndentRight,
            direct.FirstLine ?? style.FirstLine,
            direct.SpacingBefore ?? style.SpacingBefore,
            direct.SpacingAfter ?? style.SpacingAfter,
            direct.PageBreakBefore || style.PageBreakBefore,
            direct.Background ?? style.Background);
    }

    private static void ApplyParagraphFormat(Paragraph paragraph, ParagraphFormat? format)
    {
        if (format is null) return;
        if (format.Alignment is { } alignment) paragraph.TextAlignment = alignment;
        var left = format.IndentLeft ?? 0;
        var right = format.IndentRight ?? 0;
        paragraph.Margin = new Thickness(left, format.SpacingBefore ?? 0, right, format.SpacingAfter ?? 0);
        if (format.FirstLine is { } firstLine) paragraph.TextIndent = firstLine;
        if (format.PageBreakBefore) paragraph.BreakPageBefore = true;
        if (format.Background is not null) paragraph.Background = format.Background;
    }

    private static void ApplyRunFormatToParagraph(Paragraph paragraph, RunFormat format)
    {
        if (format.FontFamily is not null) paragraph.FontFamily = new FontFamily(format.FontFamily);
        if (format.FontSize is { } size) paragraph.FontSize = size;
        if (format.Bold) paragraph.FontWeight = FontWeights.Bold;
        if (format.Italic) paragraph.FontStyle = FontStyles.Italic;
        if (format.Foreground is not null) paragraph.Foreground = format.Foreground;
    }

    private static void AppendInlines(XElement container, Paragraph paragraph, ReadContext context)
    {
        foreach (var element in container.Elements())
        {
            var name = element.Name;
            if (name == W + "r")
            {
                AppendRun(element, paragraph, context);
            }
            else if (name == W + "hyperlink" || name == W + "ins" || name == W + "smartTag" ||
                     name == W + "sdt" || name == W + "sdtContent" || name == W + "fldSimple" ||
                     name == W + "dir" || name == W + "bdo")
            {
                AppendInlines(element, paragraph, context);
            }
            else if (name == W + "del" || name == W + "moveFrom")
            {
                continue;
            }
        }
    }

    private static void AppendRun(XElement element, Paragraph paragraph, ReadContext context)
    {
        var format = ReadRunFormat(element.Element(W + "rPr"));
        foreach (var child in element.Elements())
        {
            var name = child.Name;
            if (name == W + "t")
            {
                var text = child.Value;
                if (text.Length > 0) paragraph.Inlines.Add(CreateRun(text, format));
            }
            else if (name == W + "tab")
            {
                paragraph.Inlines.Add(CreateRun("\t", format));
            }
            else if (name == W + "br" || name == W + "cr")
            {
                paragraph.Inlines.Add(new LineBreak());
            }
            else if (name == W + "noBreakHyphen")
            {
                paragraph.Inlines.Add(CreateRun("-", format));
            }
            else if (name == W + "drawing")
            {
                var image = ReadImage(child, context);
                if (image is not null)
                {
                    paragraph.Inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center });
                }
            }
        }
    }

    private static Run CreateRun(string text, RunFormat? format)
    {
        var run = new Run(text);
        if (format is null) return run;
        if (format.FontFamily is not null) run.FontFamily = new FontFamily(format.FontFamily);
        if (format.FontSize is { } size) run.FontSize = size;
        if (format.Bold) run.FontWeight = FontWeights.Bold;
        if (format.Italic) run.FontStyle = FontStyles.Italic;
        if (format.Foreground is not null) run.Foreground = format.Foreground;
        if (format.Highlight is not null) run.Background = format.Highlight;
        if (format.Baseline is { } baseline) run.BaselineAlignment = baseline;
        if (format.Underline || format.Strike)
        {
            var decorations = new TextDecorationCollection();
            if (format.Underline) decorations.Add(TextDecorations.Underline[0]);
            if (format.Strike) decorations.Add(TextDecorations.Strikethrough[0]);
            decorations.Freeze();
            run.TextDecorations = decorations;
        }
        return run;
    }

    private static RunFormat? ReadRunFormat(XElement? properties)
    {
        if (properties is null) return null;
        var font = properties.Element(W + "rFonts")?.Attribute(W + "ascii")?.Value
                   ?? properties.Element(W + "rFonts")?.Attribute(W + "hAnsi")?.Value;
        double? size = null;
        var sizeValue = GetInt(properties.Element(W + "sz")?.Attribute(W + "val"), 0);
        if (sizeValue > 0) size = sizeValue * (96.0 / 72.0) / 2.0;

        var color = properties.Element(W + "color")?.Attribute(W + "val")?.Value;
        Brush? foreground = color is not null && color != "auto" ? BrushFromHex(color) : null;

        var highlightName = properties.Element(W + "highlight")?.Attribute(W + "val")?.Value;
        Brush? highlight = highlightName is not null ? HighlightBrush(highlightName) : null;

        var baseline = properties.Element(W + "vertAlign")?.Attribute(W + "val")?.Value switch
        {
            "superscript" => BaselineAlignment.Superscript,
            "subscript" => BaselineAlignment.Subscript,
            _ => (BaselineAlignment?)null
        };

        return new RunFormat(
            font,
            size,
            IsOn(properties.Element(W + "b")),
            IsOn(properties.Element(W + "i")),
            properties.Element(W + "u") is { } underline && underline.Attribute(W + "val")?.Value != "none",
            IsOn(properties.Element(W + "strike")),
            foreground,
            highlight,
            baseline);
    }

    private static bool IsOn(XElement? element)
    {
        if (element is null) return false;
        var value = element.Attribute(W + "val")?.Value;
        return value is null || value is "1" or "true" or "on";
    }

    private static Table ReadTable(XElement element, ReadContext context)
    {
        var table = new Table { CellSpacing = 0 };
        var gridWidths = element.Element(W + "tblGrid")?
            .Elements(W + "gridCol")
            .Select(col => Twips(col.Attribute(W + "w")) ?? 0)
            .ToList() ?? new List<double>();

        foreach (var width in gridWidths)
        {
            table.Columns.Add(new TableColumn { Width = width > 0 ? new GridLength(width) : GridLength.Auto });
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var rowIndex = 0;
        foreach (var rowElement in element.Elements(W + "tr"))
        {
            var row = new TableRow();
            foreach (var cellElement in rowElement.Elements(W + "tc"))
            {
                var properties = cellElement.Element(W + "tcPr");
                var cell = new TableCell
                {
                    BorderBrush = DocumentStyles.Frozen("#C9CFD8"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 4, 6, 4)
                };
                var span = GetInt(properties?.Element(W + "gridSpan")?.Attribute(W + "val"), 1);
                if (span > 1) cell.ColumnSpan = span;
                var fill = properties?.Element(W + "shd")?.Attribute(W + "fill")?.Value;
                if (fill is not null && fill != "auto") cell.Background = BrushFromHex(fill);
                if (rowIndex == 0) cell.FontWeight = FontWeights.SemiBold;

                ReadCellBlocks(cellElement, cell, context);
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
            rowIndex++;
        }
        return table;
    }

    private static void ReadCellBlocks(XElement cellElement, TableCell cell, ReadContext context)
    {
        var stack = new List<List>();
        var numId = -1;
        ReadBlocks(cellElement, cell.Blocks, context, stack, ref numId);
        if (cell.Blocks.Count == 0)
        {
            cell.Blocks.Add(DocumentStyles.CreateParagraph(context.Document));
        }
    }

    private static Image? ReadImage(XElement drawing, ReadContext context)
    {
        var embed = drawing.Descendants(A + "blip").FirstOrDefault()?.Attribute(R + "embed")?.Value;
        if (embed is null || !context.Relationships.TryGetValue(embed, out var target)) return null;
        var path = target.StartsWith('/') ? target.TrimStart('/') : "word/" + target;
        var entry = context.Archive.GetEntry(path);
        if (entry is null) return null;

        if (!context.ImageCache.TryGetValue(path, out var cached))
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(bytes);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            cached = (image, bytes);
            context.ImageCache[path] = cached;
        }

        var extent = drawing.Descendants(Wp + "extent").FirstOrDefault();
        var cx = GetLong(extent?.Attribute("cx"), 0);
        var cy = GetLong(extent?.Attribute("cy"), 0);
        var imageElement = new Image
        {
            Source = cached.Bitmap,
            Stretch = Stretch.Uniform,
            Tag = cached.Bytes
        };
        if (cx > 0) imageElement.Width = cx / 9525.0;
        if (cy > 0) imageElement.Height = cy / 9525.0;
        return imageElement;
    }

    private static Dictionary<string, string> ReadRelationships(ZipArchive archive)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var entry = archive.GetEntry("word/_rels/document.xml.rels");
        if (entry is null) return result;
        using var stream = entry.Open();
        var xml = XDocument.Load(stream);
        foreach (var relationship in xml.Root?.Elements(DocxNames.PackageRelationships + "Relationship") ?? Enumerable.Empty<XElement>())
        {
            var id = relationship.Attribute("Id")?.Value;
            var target = relationship.Attribute("Target")?.Value;
            if (id is not null && target is not null) result[id] = target;
        }
        return result;
    }

    private static Dictionary<string, ImportedStyle> ReadStyles(ZipArchive archive)
    {
        var result = new Dictionary<string, ImportedStyle>(StringComparer.Ordinal);
        var entry = archive.GetEntry("word/styles.xml");
        if (entry is null) return result;
        using var stream = entry.Open();
        var xml = XDocument.Load(stream);
        foreach (var style in xml.Root?.Elements(W + "style") ?? Enumerable.Empty<XElement>())
        {
            if (style.Attribute(W + "type")?.Value != "paragraph") continue;
            var id = style.Attribute(W + "styleId")?.Value;
            if (id is null) continue;
            var name = style.Element(W + "name")?.Attribute(W + "val")?.Value ?? id;
            var key = MapStyle(id, name);
            var format = ReadRunFormat(style.Element(W + "rPr"));
            var paragraph = ReadParagraphFormat(style.Element(W + "pPr"));
            result[id] = new ImportedStyle(key, key == DocumentStyles.Normal ? format : null, key == DocumentStyles.Normal ? paragraph : null);
        }
        return result;
    }

    private static string MapStyle(string id, string name)
    {
        var normalized = (id + "|" + name).ToLowerInvariant().Replace(" ", "").Replace("-", "");
        if (normalized.Contains("title") && !normalized.Contains("subtitle")) return DocumentStyles.Title;
        if (normalized.Contains("subtitle") || normalized.Contains("subtítulo")) return DocumentStyles.Subtitle;
        if (normalized.Contains("heading1") || normalized.Contains("título1") || normalized.Contains("titulo1") || normalized.Contains("headingi")) return DocumentStyles.Heading1;
        if (normalized.Contains("heading2") || normalized.Contains("título2") || normalized.Contains("titulo2")) return DocumentStyles.Heading2;
        if (normalized.Contains("heading3") || normalized.Contains("título3") || normalized.Contains("titulo3")) return DocumentStyles.Heading3;
        if (normalized.Contains("quote") && !normalized.Contains("intense")) return DocumentStyles.Quote;
        return DocumentStyles.Normal;
    }

    private static (Dictionary<int, int>, Dictionary<int, Dictionary<int, string>>) ReadNumbering(ZipArchive archive)
    {
        var numToAbstract = new Dictionary<int, int>();
        var abstractFormats = new Dictionary<int, Dictionary<int, string>>();
        var entry = archive.GetEntry("word/numbering.xml");
        if (entry is null) return (numToAbstract, abstractFormats);
        using var stream = entry.Open();
        var xml = XDocument.Load(stream);
        foreach (var abstractNum in xml.Root?.Elements(W + "abstractNum") ?? Enumerable.Empty<XElement>())
        {
            var id = GetInt(abstractNum.Attribute(W + "abstractNumId"), -1);
            if (id < 0) continue;
            var levels = new Dictionary<int, string>();
            foreach (var lvl in abstractNum.Elements(W + "lvl"))
            {
                var level = GetInt(lvl.Attribute(W + "ilvl"), 0);
                var format = lvl.Element(W + "numFmt")?.Attribute(W + "val")?.Value ?? "decimal";
                levels[level] = format;
            }
            abstractFormats[id] = levels;
        }
        foreach (var num in xml.Root?.Elements(W + "num") ?? Enumerable.Empty<XElement>())
        {
            var numId = GetInt(num.Attribute(W + "numId"), -1);
            var abstractId = GetInt(num.Element(W + "abstractNumId")?.Attribute(W + "val"), -1);
            if (numId >= 0 && abstractId >= 0) numToAbstract[numId] = abstractId;
        }
        return (numToAbstract, abstractFormats);
    }

    private static PageSetup ReadPageSetup(XElement body)
    {
        var section = body.Elements(W + "sectPr").LastOrDefault();
        if (section is null) return PageSetup.A4;
        var size = section.Element(W + "pgSz");
        var margin = section.Element(W + "pgMar");
        if (size is null) return PageSetup.A4;
        var width = Twips(size.Attribute(W + "w")) ?? PageSetup.A4.Width;
        var height = Twips(size.Attribute(W + "h")) ?? PageSetup.A4.Height;
        return PageSetup.FromTwips(
            width * PageSetup.DipToTwips,
            height * PageSetup.DipToTwips,
            (Twips(margin?.Attribute(W + "top")) ?? PageSetup.A4.MarginTop) * PageSetup.DipToTwips,
            (Twips(margin?.Attribute(W + "right")) ?? PageSetup.A4.MarginRight) * PageSetup.DipToTwips,
            (Twips(margin?.Attribute(W + "bottom")) ?? PageSetup.A4.MarginBottom) * PageSetup.DipToTwips,
            (Twips(margin?.Attribute(W + "left")) ?? PageSetup.A4.MarginLeft) * PageSetup.DipToTwips);
    }

    private static double? Twips(XAttribute? attribute)
    {
        if (attribute is null) return null;
        return double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value * PageSetup.TwipsToDip
            : null;
    }

    private static int GetInt(XAttribute? attribute, int fallback) =>
        int.TryParse(attribute?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static long GetLong(XAttribute? attribute, long fallback) =>
        long.TryParse(attribute?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static Brush? BrushFromHex(string hex)
    {
        try
        {
            var clean = hex.TrimStart('#');
            if (clean.Length == 6) clean = "FF" + clean;
            var color = (Color)ColorConverter.ConvertFromString("#" + clean);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return null;
        }
    }

    private static Brush? HighlightBrush(string name) => name switch
    {
        "yellow" => DocumentStyles.Frozen("#FFF2A8"),
        "green" => DocumentStyles.Frozen("#C6EFCE"),
        "cyan" => DocumentStyles.Frozen("#CCFFFF"),
        "magenta" => DocumentStyles.Frozen("#FFCCFF"),
        "blue" => DocumentStyles.Frozen("#CCE5FF"),
        "red" => DocumentStyles.Frozen("#FFC7CE"),
        "darkBlue" => DocumentStyles.Frozen("#B4C6E7"),
        "darkRed" => DocumentStyles.Frozen("#F4B6B6"),
        "lightGray" => DocumentStyles.Frozen("#EDEDED"),
        "darkGray" => DocumentStyles.Frozen("#BFBFBF"),
        "black" => DocumentStyles.Frozen("#000000"),
        "white" => DocumentStyles.Frozen("#FFFFFF"),
        _ => DocumentStyles.Frozen("#FFF2A8")
    };
}
