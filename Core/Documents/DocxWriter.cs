using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Mwrites.Core.Documents;

public static class DocxNames
{
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    public static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    public static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    public static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static XElement Element(XNamespace ns, string name, params object?[] content) => new(ns + name, content);
}

public static class DocxWriter
{
    private static readonly XNamespace W = DocxNames.W;
    private static readonly XNamespace R = DocxNames.R;
    private static readonly XNamespace Wp = DocxNames.Wp;
    private static readonly XNamespace A = DocxNames.A;
    private static readonly XNamespace Pic = DocxNames.Pic;

    private sealed record ImagePart(Image Image, byte[] Bytes, string Extension, string RelationshipId, int Id);

    public static void Save(FlowDocument document, string path)
    {
        var setup = document.Tag as PageSetup ?? PageSetup.A4;
        var images = new List<ImagePart>();
        CollectImages(document.Blocks, images);

        var numbering = new NumberingBuilder();
        var body = new XElement(W + "body");
        AppendBlocks(document.Blocks, body, numbering, images, setup);
        body.Add(BuildSectionProperties(setup));

        var documentXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "pic", Pic.NamespaceName),
                body));

        var relationships = BuildDocumentRelationships(images, numbering.HasLists);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        AddXml(archive, "[Content_Types].xml", BuildContentTypes(images, numbering.HasLists));
        AddXml(archive, "_rels/.rels", BuildPackageRelationships());
        AddXml(archive, "word/document.xml", documentXml);
        AddXml(archive, "word/_rels/document.xml.rels", relationships);
        AddXml(archive, "word/styles.xml", BuildStyles());
        AddXml(archive, "docProps/core.xml", BuildCoreProperties());
        AddXml(archive, "docProps/app.xml", BuildAppProperties());
        if (numbering.HasLists) AddXml(archive, "word/numbering.xml", numbering.Build());

        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];
            var entry = archive.CreateEntry($"word/media/image{i + 1}.{image.Extension}", CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            entryStream.Write(image.Bytes, 0, image.Bytes.Length);
        }
    }

    private static XDocument BuildDocumentRelationships(List<ImagePart> images, bool hasNumbering)
    {
        var items = new List<object>
        {
            new XElement(DocxNames.PackageRelationships + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                new XAttribute("Target", "styles.xml"))
        };
        if (hasNumbering)
        {
            items.Add(new XElement(DocxNames.PackageRelationships + "Relationship",
                new XAttribute("Id", "rId2"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering"),
                new XAttribute("Target", "numbering.xml")));
        }
        foreach (var image in images)
        {
            items.Add(new XElement(DocxNames.PackageRelationships + "Relationship",
                new XAttribute("Id", image.RelationshipId),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", $"media/image{image.Id}.{image.Extension}")));
        }
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(DocxNames.PackageRelationships + "Relationships", items));
    }

    private static XDocument BuildPackageRelationships() => new(
        new XDeclaration("1.0", "UTF-8", "yes"),
        new XElement(DocxNames.PackageRelationships + "Relationships",
            new XElement(DocxNames.PackageRelationships + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                new XAttribute("Target", "word/document.xml")),
            new XElement(DocxNames.PackageRelationships + "Relationship",
                new XAttribute("Id", "rId2"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"),
                new XAttribute("Target", "docProps/core.xml")),
            new XElement(DocxNames.PackageRelationships + "Relationship",
                new XAttribute("Id", "rId3"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties"),
                new XAttribute("Target", "docProps/app.xml"))));

    private static XDocument BuildContentTypes(List<ImagePart> images, bool hasNumbering)
    {
        var items = new List<object>
        {
            new XElement(DocxNames.ContentTypes + "Default",
                new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(DocxNames.ContentTypes + "Default",
                new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml"))
        };
        if (images.Any(i => i.Extension == "png"))
        {
            items.Add(new XElement(DocxNames.ContentTypes + "Default",
                new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")));
        }
        if (images.Any(i => i.Extension is "jpeg" or "jpg"))
        {
            items.Add(new XElement(DocxNames.ContentTypes + "Default",
                new XAttribute("Extension", "jpeg"), new XAttribute("ContentType", "image/jpeg")));
        }
        items.AddRange(new[]
        {
            new XElement(DocxNames.ContentTypes + "Override",
                new XAttribute("PartName", "/word/document.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
            new XElement(DocxNames.ContentTypes + "Override",
                new XAttribute("PartName", "/word/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")),
            new XElement(DocxNames.ContentTypes + "Override",
                new XAttribute("PartName", "/docProps/core.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml")),
            new XElement(DocxNames.ContentTypes + "Override",
                new XAttribute("PartName", "/docProps/app.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.extended-properties+xml"))
        });
        if (hasNumbering)
        {
            items.Add(new XElement(DocxNames.ContentTypes + "Override",
                new XAttribute("PartName", "/word/numbering.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml")));
        }
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(DocxNames.ContentTypes + "Types", items));
    }

    private static void AddXml(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        document.Save(writer);
    }

    private static void CollectImages(BlockCollection blocks, List<ImagePart> images)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    CollectImages(paragraph.Inlines, images);
                    break;
                case List list:
                    foreach (var item in list.ListItems) CollectImages(item.Blocks, images);
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                    foreach (var cell in row.Cells)
                        CollectImages(cell.Blocks, images);
                    break;
                case Section section:
                    CollectImages(section.Blocks, images);
                    break;
            }
        }
    }

    private static void CollectImages(InlineCollection inlines, List<ImagePart> images)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case InlineUIContainer { Child: Image image }:
                    var data = ReadImage(image);
                    if (data is null) continue;
                    var id = images.Count + 1;
                    images.Add(new ImagePart(image, data.Value.Bytes, data.Value.Extension, $"rIdImg{id}", id));
                    break;
                case Span span:
                    CollectImages(span.Inlines, images);
                    break;
                case Figure figure:
                    CollectImages(figure.Blocks, images);
                    break;
                case Floater floater:
                    CollectImages(floater.Blocks, images);
                    break;
            }
        }
    }

    private static (byte[] Bytes, string Extension)? ReadImage(Image image)
    {
        if (image.Tag is byte[] tagged && tagged.Length > 0)
        {
            return (tagged, DetectExtension(tagged));
        }
        if (image.Source is BitmapImage bitmap && bitmap.UriSource is { IsFile: true } uri && File.Exists(uri.LocalPath))
        {
            var bytes = File.ReadAllBytes(uri.LocalPath);
            return (bytes, DetectExtension(bytes));
        }
        if (image.Source is BitmapSource source)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var memory = new MemoryStream();
            encoder.Save(memory);
            return (memory.ToArray(), "png");
        }
        return null;
    }

    private static string DetectExtension(byte[] data)
    {
        if (data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8) return "jpeg";
        return "png";
    }

    private static void AppendBlocks(BlockCollection blocks, XElement parent, NumberingBuilder numbering, List<ImagePart> images, PageSetup setup)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    parent.Add(BuildParagraph(paragraph, numbering, images, null, null));
                    break;
                case List list:
                    AppendList(list, parent, numbering, images, 0, setup);
                    break;
                case Table table:
                    parent.Add(BuildTable(table, numbering, images, setup));
                    break;
                case Section section:
                    AppendBlocks(section.Blocks, parent, numbering, images, setup);
                    break;
            }
        }
    }

    private static void AppendList(List list, XElement parent, NumberingBuilder numbering, List<ImagePart> images, int level, PageSetup setup)
    {
        var numId = numbering.NewList(list.MarkerStyle is TextMarkerStyle.Disc or TextMarkerStyle.Circle or TextMarkerStyle.Square or TextMarkerStyle.Box);
        foreach (var item in list.ListItems)
        {
            var first = true;
            foreach (var block in item.Blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        parent.Add(BuildParagraph(paragraph, numbering, images, first ? level : null, first ? numId : (int?)null));
                        first = false;
                        break;
                    case List nested:
                        AppendList(nested, parent, numbering, images, level + 1, setup);
                        first = false;
                        break;
                    case Table table:
                        parent.Add(BuildTable(table, numbering, images, setup));
                        first = false;
                        break;
                }
            }
        }
    }

    private static XElement BuildParagraph(Paragraph paragraph, NumberingBuilder numbering, List<ImagePart> images, int? listLevel, int? numId)
    {
        var properties = new XElement(W + "pPr");
        var styleId = StyleIdFor(DocumentStyles.StyleKeyOf(paragraph));
        if (styleId is not null)
        {
            properties.Add(new XElement(W + "pStyle", new XAttribute(W + "val", styleId)));
        }
        if (paragraph.TextAlignment != TextAlignment.Left)
        {
            properties.Add(new XElement(W + "jc", new XAttribute(W + "val", AlignmentValue(paragraph.TextAlignment))));
        }

        var indent = new List<object>();
        var left = Math.Round(paragraph.Margin.Left * PageSetup.DipToTwips);
        var right = Math.Round(paragraph.Margin.Right * PageSetup.DipToTwips);
        if (left > 0) indent.Add(new XAttribute(W + "left", (long)left));
        if (right > 0 && !double.IsNaN(right)) indent.Add(new XAttribute(W + "right", (long)right));
        if (paragraph.TextIndent > 0.1)
        {
            indent.Add(new XAttribute(W + "firstLine", (long)Math.Round(paragraph.TextIndent * PageSetup.DipToTwips)));
        }
        else if (paragraph.TextIndent < -0.1)
        {
            indent.Add(new XAttribute(W + "hanging", (long)Math.Round(-paragraph.TextIndent * PageSetup.DipToTwips)));
        }
        if (indent.Count > 0) properties.Add(new XElement(W + "ind", indent));

        var spacing = new List<object>();
        if (paragraph.Margin.Top != 0)
            spacing.Add(new XAttribute(W + "before", (long)Math.Round(paragraph.Margin.Top * PageSetup.DipToTwips)));
        if (paragraph.Margin.Bottom != 0)
            spacing.Add(new XAttribute(W + "after", (long)Math.Round(paragraph.Margin.Bottom * PageSetup.DipToTwips)));
        if (spacing.Count > 0) properties.Add(new XElement(W + "spacing", spacing));

        if (paragraph.BreakPageBefore)
        {
            properties.Add(new XElement(W + "pageBreakBefore"));
        }
        if (listLevel is not null && numId is not null)
        {
            properties.Add(new XElement(W + "numPr",
                new XElement(W + "ilvl", new XAttribute(W + "val", listLevel.Value)),
                new XElement(W + "numId", new XAttribute(W + "val", numId.Value))));
        }

        var element = new XElement(W + "p", properties);
        AppendInlines(paragraph.Inlines, element, images);
        return element;
    }

    private static void AppendInlines(InlineCollection inlines, XElement parent, List<ImagePart> images)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    // Run.Text excludes the list marker ("•\t") that TextRange includes for
                    // paragraphs inside a ListItem; numbering is rebuilt from numPr instead.
                    var text = run.Text;
                    if (text.Length == 0) continue;
                    var runElement = new XElement(W + "r", BuildRunProperties(run),
                        new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));
                    parent.Add(runElement);
                    break;
                case LineBreak:
                    parent.Add(new XElement(W + "r", new XElement(W + "br")));
                    break;
                case InlineUIContainer { Child: Image image }:
                    var part = images.FirstOrDefault(i => ReferenceEquals(i.Image, image));
                    if (part is not null) parent.Add(new XElement(W + "r", BuildDrawing(part, image)));
                    break;
                case Span span:
                    AppendInlines(span.Inlines, parent, images);
                    break;
                case Figure figure:
                    foreach (var block in figure.Blocks)
                    {
                        if (block is Paragraph paragraph)
                        {
                            foreach (var child in paragraph.Inlines.ToList())
                            {
                                var wrapper = new Span();
                                wrapper.Inlines.Add(child);
                                AppendInlines(wrapper.Inlines, parent, images);
                            }
                        }
                    }
                    break;
            }
        }
    }

    private static XElement? BuildRunProperties(Run run)
    {
        var properties = new XElement(W + "rPr");
        var hasAny = false;

        if (run.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily family)
        {
            properties.Add(new XElement(W + "rFonts",
                new XAttribute(W + "ascii", family.Source),
                new XAttribute(W + "hAnsi", family.Source)));
            hasAny = true;
        }
        if (run.ReadLocalValue(TextElement.FontSizeProperty) is double size && size > 0)
        {
            var halfPoints = (int)Math.Round(size * 72.0 / 96.0 * 2);
            properties.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
            properties.Add(new XElement(W + "szCs", new XAttribute(W + "val", halfPoints)));
            hasAny = true;
        }
        if (run.ReadLocalValue(TextElement.FontWeightProperty) is FontWeight weight && weight != FontWeights.Normal)
        {
            properties.Add(new XElement(W + "b"));
            hasAny = true;
        }
        if (run.ReadLocalValue(TextElement.FontStyleProperty) is FontStyle style && style != FontStyles.Normal)
        {
            properties.Add(new XElement(W + "i"));
            hasAny = true;
        }
        if (run.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush foreground)
        {
            properties.Add(new XElement(W + "color", new XAttribute(W + "val", ToHex(foreground.Color))));
            hasAny = true;
        }
        if (run.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush background)
        {
            properties.Add(new XElement(W + "highlight", new XAttribute(W + "val", HighlightValue(background.Color))));
            hasAny = true;
        }
        if (run.ReadLocalValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations)
        {
            if (decorations.Any(d => d.Location == TextDecorationLocation.Underline))
            {
                properties.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
                hasAny = true;
            }
            if (decorations.Any(d => d.Location == TextDecorationLocation.Strikethrough))
            {
                properties.Add(new XElement(W + "strike"));
                hasAny = true;
            }
        }
        if (run.ReadLocalValue(Inline.BaselineAlignmentProperty) is BaselineAlignment baseline)
        {
            if (baseline == BaselineAlignment.Superscript)
            {
                properties.Add(new XElement(W + "vertAlign", new XAttribute(W + "val", "superscript")));
                hasAny = true;
            }
            else if (baseline == BaselineAlignment.Subscript)
            {
                properties.Add(new XElement(W + "vertAlign", new XAttribute(W + "val", "subscript")));
                hasAny = true;
            }
        }
        return hasAny ? properties : null;
    }

    private static XElement BuildDrawing(ImagePart part, Image image)
    {
        var width = double.IsNaN(image.Width) || image.Width <= 0 ? (image.Source?.Width ?? 320) : image.Width;
        var height = double.IsNaN(image.Height) || image.Height <= 0 ? (image.Source?.Height ?? 200) : image.Height;
        var cx = (long)Math.Round(width * 9525);
        var cy = (long)Math.Round(height * 9525);

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "docPr", new XAttribute("id", part.Id), new XAttribute("name", $"Imagen {part.Id}")),
                new XElement(A + "graphic",
                    new XElement(A + "graphicData", new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                        new XElement(Pic + "pic",
                            new XElement(Pic + "nvPicPr",
                                new XElement(Pic + "cNvPr", new XAttribute("id", part.Id), new XAttribute("name", $"imagen{part.Id}.{part.Extension}")),
                                new XElement(Pic + "cNvPicPr")),
                            new XElement(Pic + "blipFill",
                                new XElement(A + "blip", new XAttribute(R + "embed", part.RelationshipId)),
                                new XElement(A + "stretch", new XElement(A + "fillRect"))),
                            new XElement(Pic + "spPr",
                                new XElement(A + "xfrm",
                                    new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                                    new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                                new XElement(A + "prstGeom", new XAttribute("prst", "rect"),
                                    new XElement(A + "avLst"))))))));
    }

    private static XElement BuildTable(Table table, NumberingBuilder numbering, List<ImagePart> images, PageSetup setup)
    {
        var rows = table.RowGroups.SelectMany(g => g.Rows).ToList();
        var columns = Math.Max(1, table.Columns.Count > 0 ? table.Columns.Count : rows.Select(r => r.Cells.Count).DefaultIfEmpty(1).Max());
        var totalWidth = setup.ContentWidth <= 0 ? 600 : setup.ContentWidth;
        var widths = new List<double>();
        for (var i = 0; i < columns; i++)
        {
            // WPF defaults to Auto (Value 1), which is not a real pixel width; fall back to
            // an even split so the table keeps usable column widths in Word and on reload.
            var length = i < table.Columns.Count ? table.Columns[i].Width : GridLength.Auto;
            var width = length.GridUnitType == GridUnitType.Pixel ? length.Value : double.NaN;
            widths.Add(double.IsNaN(width) || width <= 0 ? totalWidth / columns : width);
        }

        var borders = new XElement(W + "tblBorders",
            Border("top"), Border("left"), Border("bottom"), Border("right"), Border("insideH"), Border("insideV"));

        var tableElement = new XElement(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "tblW", new XAttribute(W + "w", 5000), new XAttribute(W + "type", "pct")),
                borders,
                new XElement(W + "tblLook",
                    new XAttribute(W + "val", "04A0"),
                    new XAttribute(W + "firstRow", 1),
                    new XAttribute(W + "lastRow", 0),
                    new XAttribute(W + "firstColumn", 1),
                    new XAttribute(W + "lastColumn", 0),
                    new XAttribute(W + "noHBand", 0),
                    new XAttribute(W + "noVBand", 1))),
            new XElement(W + "tblGrid", widths.Select(w => new XElement(W + "gridCol", new XAttribute(W + "w", (long)Math.Round(w * PageSetup.DipToTwips))))));

        foreach (var row in rows)
        {
            var rowElement = new XElement(W + "tr");
            var cellIndex = 0;
            foreach (var cell in row.Cells)
            {
                var width = cellIndex < widths.Count ? widths[cellIndex] : widths[^1];
                var properties = new XElement(W + "tcPr",
                    new XElement(W + "tcW", new XAttribute(W + "w", (long)Math.Round(width * PageSetup.DipToTwips)), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "vAlign", new XAttribute(W + "val", "top")));
                if (cell.Background is SolidColorBrush background)
                {
                    properties.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", ToHex(background.Color))));
                }
                var cellElement = new XElement(W + "tc", properties);
                if (cell.Blocks.Count == 0)
                {
                    cellElement.Add(new XElement(W + "p"));
                }
                else
                {
                    AppendBlocks(cell.Blocks, cellElement, numbering, images, setup);
                }
                rowElement.Add(cellElement);
                cellIndex += Math.Max(1, cell.ColumnSpan);
            }
            tableElement.Add(rowElement);
        }
        return tableElement;
    }

    private static XElement Border(string side) => new(W + side,
        new XAttribute(W + "val", "single"),
        new XAttribute(W + "sz", 4),
        new XAttribute(W + "space", 0),
        new XAttribute(W + "color", "C9CFD8"));

    private static string? StyleIdFor(string key) => key switch
    {
        DocumentStyles.Normal => null,
        DocumentStyles.Title => "Title",
        DocumentStyles.Subtitle => "Subtitle",
        DocumentStyles.Heading1 => "Heading1",
        DocumentStyles.Heading2 => "Heading2",
        DocumentStyles.Heading3 => "Heading3",
        DocumentStyles.Quote => "Quote",
        _ => null
    };

    private static string AlignmentValue(TextAlignment alignment) => alignment switch
    {
        TextAlignment.Center => "center",
        TextAlignment.Right => "right",
        TextAlignment.Justify => "both",
        _ => "left"
    };

    private static string ToHex(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string HighlightValue(Color color)
    {
        var options = new (string Name, Color Color)[]
        {
            ("yellow", Colors.Yellow), ("green", Colors.Lime), ("cyan", Colors.Cyan),
            ("magenta", Colors.Magenta), ("blue", Colors.Blue), ("red", Colors.Red),
            ("lightGray", Colors.LightGray), ("darkGray", Colors.Gray)
        };
        var best = options.OrderBy(o => Distance(o.Color, color)).First();
        return best.Name;
    }

    private static double Distance(Color a, Color b) =>
        Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2);

    private static XElement BuildSectionProperties(PageSetup setup)
    {
        var width = (long)Math.Round(setup.Width * PageSetup.DipToTwips);
        var height = (long)Math.Round(setup.Height * PageSetup.DipToTwips);
        return new XElement(W + "sectPr",
            new XElement(W + "pgSz",
                new XAttribute(W + "w", width),
                new XAttribute(W + "h", height),
                new XAttribute(W + "orient", setup.Landscape ? "landscape" : "portrait")),
            new XElement(W + "pgMar",
                new XAttribute(W + "top", (long)Math.Round(setup.MarginTop * PageSetup.DipToTwips)),
                new XAttribute(W + "right", (long)Math.Round(setup.MarginRight * PageSetup.DipToTwips)),
                new XAttribute(W + "bottom", (long)Math.Round(setup.MarginBottom * PageSetup.DipToTwips)),
                new XAttribute(W + "left", (long)Math.Round(setup.MarginLeft * PageSetup.DipToTwips)),
                new XAttribute(W + "header", 720),
                new XAttribute(W + "footer", 720),
                new XAttribute(W + "gutter", 0)),
            new XElement(W + "cols", new XAttribute(W + "space", 708)),
            new XElement(W + "docGrid", new XAttribute(W + "linePitch", 360)));
    }

    private static XDocument BuildStyles()
    {
        XElement Style(string id, string name, int? size, bool bold = false, bool italic = false,
            string color = "000000", string? font = null, int before = 0, int after = 0,
            int outline = -1, bool underline = false, int? indentLeft = null)
        {
            var rPr = new XElement(W + "rPr");
            if (font is not null) rPr.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", font), new XAttribute(W + "hAnsi", font)));
            if (bold) rPr.Add(new XElement(W + "b"));
            if (italic) rPr.Add(new XElement(W + "i"));
            if (underline) rPr.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
            if (size is not null)
            {
                rPr.Add(new XElement(W + "sz", new XAttribute(W + "val", size)));
                rPr.Add(new XElement(W + "szCs", new XAttribute(W + "val", size)));
            }
            rPr.Add(new XElement(W + "color", new XAttribute(W + "val", color)));

            var pPr = new XElement(W + "pPr");
            if (before > 0 || after > 0)
            {
                pPr.Add(new XElement(W + "spacing",
                    new XAttribute(W + "before", before),
                    new XAttribute(W + "after", after)));
            }
            if (outline >= 0)
            {
                pPr.Add(new XElement(W + "keepNext"));
                pPr.Add(new XElement(W + "outlineLvl", new XAttribute(W + "val", outline)));
            }
            if (indentLeft is not null)
            {
                pPr.Add(new XElement(W + "ind", new XAttribute(W + "left", indentLeft)));
            }

            var style = new XElement(W + "style",
                new XAttribute(W + "type", "paragraph"),
                new XAttribute(W + "styleId", id),
                new XElement(W + "name", new XAttribute(W + "val", name)),
                id == "Normal" ? null : new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")),
                new XElement(W + "next", new XAttribute(W + "val", "Normal")),
                new XElement(W + "qFormat"),
                pPr,
                rPr);
            return style;
        }

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "styles",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XElement(W + "docDefaults",
                    new XElement(W + "rPrDefault",
                        new XElement(W + "rPr",
                            new XElement(W + "rFonts", new XAttribute(W + "ascii", "Calibri"), new XAttribute(W + "hAnsi", "Calibri"), new XAttribute(W + "cs", "Calibri")),
                            new XElement(W + "sz", new XAttribute(W + "val", 22)),
                            new XElement(W + "szCs", new XAttribute(W + "val", 22)),
                            new XElement(W + "lang", new XAttribute(W + "val", "es-ES")))),
                    new XElement(W + "pPrDefault",
                        new XElement(W + "pPr",
                            new XElement(W + "spacing", new XAttribute(W + "after", 0), new XAttribute(W + "line", 240), new XAttribute(W + "lineRule", "auto"))))),
                Style("Normal", "Normal", 22),
                Style("Title", "Title", 56, color: "1F3864", font: "Calibri Light", after: 120),
                Style("Subtitle", "Subtitle", 28, italic: true, color: "595959", font: "Calibri Light", after: 280),
                Style("Heading1", "heading 1", 32, bold: true, color: "2E74B5", before: 320, after: 120, outline: 0),
                Style("Heading2", "heading 2", 26, bold: true, color: "2E74B5", before: 240, after: 80, outline: 1),
                Style("Heading3", "heading 3", 24, bold: true, color: "1F3864", before: 200, after: 60, outline: 2),
                Style("Quote", "Quote", 22, italic: true, color: "595959", indentLeft: 360)));
    }

    private static XDocument BuildCoreProperties()
    {
        XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace dcterms = "http://purl.org/dc/terms/";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(cp + "coreProperties",
                new XAttribute(XNamespace.Xmlns + "cp", cp.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "dc", dc.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "dcterms", dcterms.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "xsi", xsi.NamespaceName),
                new XElement(dc + "title", "Documento"),
                new XElement(dc + "creator", "Mwrites"),
                new XElement(dc + "lastModifiedBy", "Mwrites"),
                new XElement(dcterms + "created",
                    new XAttribute(xsi + "type", "dcterms:W3CDTF"),
                    DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))));
    }

    private static XDocument BuildAppProperties()
    {
        XNamespace ep = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        XNamespace vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ep + "Properties",
                new XAttribute(XNamespace.Xmlns + "vt", vt.NamespaceName),
                new XElement(ep + "Application", "Mwrites"),
                new XElement(ep + "AppVersion", "0.1")));
    }

    private sealed class NumberingBuilder
    {
        private readonly List<XElement> _abstracts = new();
        private readonly List<XElement> _nums = new();
        private readonly Dictionary<bool, int> _abstractByKind = new();

        public bool HasLists => _nums.Count > 0;

        public int NewList(bool bullet)
        {
            var abstractId = GetAbstract(bullet);
            var numId = _nums.Count + 1;
            _nums.Add(new XElement(W + "num",
                new XAttribute(W + "numId", numId),
                new XElement(W + "abstractNumId", new XAttribute(W + "val", abstractId))));
            return numId;
        }

        private int GetAbstract(bool bullet)
        {
            if (_abstractByKind.TryGetValue(bullet, out var existing)) return existing;
            var id = _abstractByKind.Count;
            var abstractElement = new XElement(W + "abstractNum",
                new XAttribute(W + "abstractNumId", id),
                new XElement(W + "multiLevelType", new XAttribute(W + "val", "hybridMultilevel")));
            for (var level = 0; level < 9; level++)
            {
                var levelElement = new XElement(W + "lvl", new XAttribute(W + "ilvl", level),
                    new XElement(W + "start", new XAttribute(W + "val", 1)),
                    new XElement(W + "numFmt", new XAttribute(W + "val", bullet ? "bullet" : "decimal")),
                    new XElement(W + "lvlText", new XAttribute(W + "val", bullet ? "•" : $"%{level + 1}.")),
                    new XElement(W + "lvlJc", new XAttribute(W + "val", "left")),
                    new XElement(W + "pPr",
                        new XElement(W + "ind",
                            new XAttribute(W + "left", 720 * (level + 1)),
                            new XAttribute(W + "hanging", 360))));
                if (bullet)
                {
                    levelElement.Add(new XElement(W + "rPr",
                        new XElement(W + "rFonts", new XAttribute(W + "ascii", "Symbol"), new XAttribute(W + "hAnsi", "Symbol"), new XAttribute(W + "hint", "default"))));
                }
                abstractElement.Add(levelElement);
            }
            _abstracts.Add(abstractElement);
            _abstractByKind[bullet] = id;
            return id;
        }

        public XDocument Build() => new(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "numbering",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                _abstracts,
                _nums));
    }
}
