using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Mwrites.Portable.Core;

/// <summary>
/// Lectura y escritura de documentos sin depender de WPF. Implementa un subconjunto
/// de OOXML suficiente para párrafos, estilos, negrita, cursiva, subrayado, fuente,
/// tamaño, color, alineación, listas y tamaño de página.
/// </summary>
public static class PortableDocumentService
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";
    private static readonly string[] Supported = { ".docx", ".txt", ".mwdoc" };

    public static bool IsSupported(string path) =>
        Supported.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static string[] Filters => Supported;

    public static PortableDocument Open(string path)
    {
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".docx" => ReadDocx(path),
            ".txt" => ReadText(path),
            ".mwdoc" => ReadJson(path),
            _ => throw new NotSupportedException($"Formato no compatible: {extension}")
        };
    }

    public static void Save(PortableDocument document, string path)
    {
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        switch (extension)
        {
            case ".docx":
                WriteDocx(document, path);
                break;
            case ".txt":
                // Se guarda lo que se ve en el editor, incluidas las marcas de estilo (#, >, -).
                File.WriteAllText(path, document.EditorText.Replace("\r\n", "\n").Replace('\r', '\n')
                    .Replace("\n", Environment.NewLine), new UTF8Encoding(true));
                break;
            case ".mwdoc":
                WriteJson(document, path);
                break;
            default:
                throw new NotSupportedException($"Formato no compatible: {extension}");
        }
    }

    public static PortableDocument FromEditorText(string text)
    {
        var document = new PortableDocument();
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
            document.Paragraphs.Add(PortableMarkers.ParseLine(line));
        return document;
    }

    // ---------------- TXT ----------------

    private static PortableDocument ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        return FromEditorText(text);
    }

    // ---------------- MWDOC (JSON nativo) ----------------

    private static PortableDocument ReadJson(string path)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dto = JsonSerializer.Deserialize<DocumentDto>(File.ReadAllText(path), options)
                  ?? throw new InvalidDataException("El archivo MWDOC está vacío.");
        if (dto.Paragraphs is null) throw new InvalidDataException("El archivo MWDOC no tiene párrafos.");

        var document = new PortableDocument { Setup = dto.Setup ?? new PortablePageSetup() };
        foreach (var item in dto.Paragraphs)
        {
            document.Paragraphs.Add(new PortableParagraph
            {
                Style = item.Style,
                Alignment = item.Alignment,
                List = item.List,
                Runs = item.Runs?.Count > 0
                    ? item.Runs.Select(run => new PortableRun
                    {
                        Text = run.Text ?? string.Empty,
                        Bold = run.Bold,
                        Italic = run.Italic,
                        Underline = run.Underline,
                        FontFamily = run.FontFamily,
                        FontSizePt = run.FontSizePt,
                        ColorHex = run.ColorHex
                    }).ToList()
                    : new List<PortableRun> { new() { Text = item.Text ?? string.Empty } }
            });
        }
        return document;
    }

    private static void WriteJson(PortableDocument document, string path)
    {
        var dto = new DocumentDto
        {
            Format = "Mwrites",
            Version = "1.1",
            Setup = document.Setup,
            Paragraphs = document.Paragraphs.Select(p => new ParagraphDto
            {
                Style = p.Style,
                Alignment = p.Alignment,
                List = p.List,
                Text = p.Text,
                Runs = p.Runs.Select(run => new RunDto
                {
                    Text = run.Text,
                    Bold = run.Bold,
                    Italic = run.Italic,
                    Underline = run.Underline,
                    FontFamily = run.FontFamily,
                    FontSizePt = run.FontSizePt,
                    ColorHex = run.ColorHex
                }).ToList()
            }).ToList()
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, options), new UTF8Encoding(false));
    }

    // ---------------- DOCX ----------------

    private static PortableDocument ReadDocx(string path)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml")
                    ?? throw new InvalidDataException("No es un DOCX válido: falta word/document.xml.");
        XDocument xml;
        using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
        {
            xml = XDocument.Load(reader);
        }

        var document = new PortableDocument();
        ReadPageSetup(xml, document.Setup);

        foreach (var paragraph in xml.Descendants(W + "p"))
        {
            var properties = paragraph.Element(W + "pPr");
            var result = new PortableParagraph
            {
                Style = ReadStyle(properties?.Element(W + "pStyle")?.Attribute(W + "val")?.Value),
                Alignment = ReadAlignment(properties?.Element(W + "jc")?.Attribute(W + "val")?.Value),
                List = properties?.Element(W + "numPr") is not null
                    ? PortableListKind.Numbered
                    : PortableListKind.None
            };

            foreach (var run in paragraph.Descendants(W + "r"))
            {
                var runProperties = run.Element(W + "rPr");
                var text = new StringBuilder();
                foreach (var node in run.Nodes())
                {
                    if (node is not XElement element) continue;
                    if (element.Name == W + "t") text.Append(element.Value);
                    else if (element.Name == W + "tab") text.Append('\t');
                    else if (element.Name == W + "br" || element.Name == W + "cr") text.Append('\n');
                }
                if (text.Length == 0) continue;
                result.Runs.Add(new PortableRun
                {
                    Text = text.ToString(),
                    Bold = IsOn(runProperties?.Element(W + "b")),
                    Italic = IsOn(runProperties?.Element(W + "i")),
                    Underline = IsOn(runProperties?.Element(W + "u")),
                    FontFamily = runProperties?.Element(W + "rFonts")?.Attribute(W + "ascii")?.Value,
                    FontSizePt = ReadDouble(runProperties?.Element(W + "sz")?.Attribute(W + "val")?.Value) / 2,
                    ColorHex = CleanColor(runProperties?.Element(W + "color")?.Attribute(W + "val")?.Value)
                });
            }

            if (result.Runs.Count == 0) result.Runs.Add(new PortableRun());
            document.Paragraphs.Add(result);
        }

        if (document.Paragraphs.Count == 0) document.Paragraphs.Add(PortableParagraph.Plain(string.Empty));
        return document;
    }

    private static void WriteDocx(PortableDocument document, string path)
    {
        var relationships =
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\" />" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\" />" +
            "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\" />" +
            "</Relationships>";

        var contentTypes =
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" />" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\" />" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\" />" +
            "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\" />" +
            "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\" />" +
            "</Types>";

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var core =
            "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
            "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
            "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
            "<dc:title>Documento Mwrites</dc:title><dc:creator>MSoft</dc:creator><cp:lastModifiedBy>MSoft</cp:lastModifiedBy>" +
            $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{now}</dcterms:created>" +
            $"<dcterms:modified xsi:type=\"dcterms:W3CDTF\">{now}</dcterms:modified></cp:coreProperties>";

        var app =
            "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" " +
            "xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
            "<Application>Mwrites Portable 1.1</Application></Properties>";

        var documentXml = BuildDocumentXml(document);
        var emptyRelationships =
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\" />";

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", contentTypes);
        WriteEntry(archive, "_rels/.rels", relationships);
        WriteEntry(archive, "word/_rels/document.xml.rels", emptyRelationships);
        WriteEntry(archive, "docProps/core.xml", core);
        WriteEntry(archive, "docProps/app.xml", app);
        WriteEntry(archive, "word/document.xml", documentXml.ToString(SaveOptions.DisableFormatting));
    }

    private static XDocument BuildDocumentXml(PortableDocument document)
    {
        XElement Para(PortableParagraph paragraph)
        {
            var properties = new XElement(W + "pPr");
            var styleId = paragraph.Style switch
            {
                PortableStyle.Heading1 => "Heading1",
                PortableStyle.Heading2 => "Heading2",
                PortableStyle.Heading3 => "Heading3",
                PortableStyle.Subtitle => "Subtitle",
                PortableStyle.Quote => "Quote",
                _ => null
            };
            if (styleId is not null) properties.Add(new XElement(W + "pStyle", new XAttribute(W + "val", styleId)));
            var alignment = paragraph.Alignment switch
            {
                PortableAlignment.Center => "center",
                PortableAlignment.Right => "right",
                PortableAlignment.Justify => "both",
                _ => "left"
            };
            properties.Add(new XElement(W + "jc", new XAttribute(W + "val", alignment)));
            if (paragraph.List != PortableListKind.None)
                properties.Add(new XElement(W + "numPr",
                    new XElement(W + "ilvl", new XAttribute(W + "val", "0")),
                    new XElement(W + "numId", new XAttribute(W + "val", paragraph.List == PortableListKind.Bullet ? "1" : "2"))));

            var element = new XElement(W + "p", properties);
            foreach (var run in paragraph.Runs)
            {
                var runProperties = new XElement(W + "rPr");
                if (run.Bold) runProperties.Add(new XElement(W + "b"));
                if (run.Italic) runProperties.Add(new XElement(W + "i"));
                if (run.Underline) runProperties.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
                if (!string.IsNullOrWhiteSpace(run.FontFamily))
                    runProperties.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", run.FontFamily),
                        new XAttribute(W + "hAnsi", run.FontFamily)));
                if (run.FontSizePt is > 0)
                    runProperties.Add(new XElement(W + "sz", new XAttribute(W + "val",
                        Math.Round(run.FontSizePt.Value * 2).ToString(CultureInfo.InvariantCulture))));
                if (!string.IsNullOrWhiteSpace(run.ColorHex))
                    runProperties.Add(new XElement(W + "color", new XAttribute(W + "val", run.ColorHex)));
                element.Add(new XElement(W + "r", runProperties,
                    new XElement(W + "t", new XAttribute(Xml + "space", "preserve"), run.Text)));
            }
            return element;
        }

        var body = new XElement(W + "body", document.Paragraphs.Select(Para));
        var setup = document.Setup;
        var width = (setup.Landscape ? setup.HeightPt : setup.WidthPt) * 20;
        var height = (setup.Landscape ? setup.WidthPt : setup.HeightPt) * 20;
        var margin = setup.MarginMm * 72.0 / 25.4 * 20;
        body.Add(new XElement(W + "sectPr",
            new XElement(W + "pgSz",
                new XAttribute(W + "w", Math.Round(width).ToString(CultureInfo.InvariantCulture)),
                new XAttribute(W + "h", Math.Round(height).ToString(CultureInfo.InvariantCulture)),
                setup.Landscape ? new XAttribute(W + "orient", "landscape") : null),
            new XElement(W + "pgMar",
                new XAttribute(W + "top", Math.Round(margin).ToString(CultureInfo.InvariantCulture)),
                new XAttribute(W + "right", Math.Round(margin).ToString(CultureInfo.InvariantCulture)),
                new XAttribute(W + "bottom", Math.Round(margin).ToString(CultureInfo.InvariantCulture)),
                new XAttribute(W + "left", Math.Round(margin).ToString(CultureInfo.InvariantCulture)))));

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName), body));
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void ReadPageSetup(XDocument xml, PortablePageSetup setup)
    {
        var section = xml.Descendants(W + "sectPr").FirstOrDefault();
        var size = section?.Element(W + "pgSz");
        var margin = section?.Element(W + "pgMar");
        var width = ReadDouble(size?.Attribute(W + "w")?.Value);
        var height = ReadDouble(size?.Attribute(W + "h")?.Value);
        if (width > 0 && height > 0)
        {
            setup.WidthPt = width / 20;
            setup.HeightPt = height / 20;
            setup.Landscape = width > height;
            setup.SizeName = Math.Abs(width / 20 - 595.3) < 12 ? "A4" :
                Math.Abs(width / 20 - 612) < 12 ? "Carta" : "Personalizado";
        }
        var left = ReadDouble(margin?.Attribute(W + "left")?.Value);
        if (left > 0) setup.MarginMm = left / 20 * 25.4 / 72;
    }

    private static PortableStyle ReadStyle(string? value) => value switch
    {
        "Heading1" or "1" or "Titulo1" => PortableStyle.Heading1,
        "Heading2" or "2" or "Titulo2" => PortableStyle.Heading2,
        "Heading3" or "3" or "Titulo3" => PortableStyle.Heading3,
        "Subtitle" => PortableStyle.Subtitle,
        "Quote" or "INTENSEQUOTE" or "Cita" => PortableStyle.Quote,
        _ => PortableStyle.Normal
    };

    private static PortableAlignment ReadAlignment(string? value) => value switch
    {
        "center" => PortableAlignment.Center,
        "right" or "end" => PortableAlignment.Right,
        "both" or "justify" => PortableAlignment.Justify,
        _ => PortableAlignment.Left
    };

    private static bool IsOn(XElement? element)
    {
        if (element is null) return false;
        var value = element.Attribute(W + "val")?.Value;
        return value is not ("0" or "false" or "off" or "none");
    }

    private static double ReadDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static string? CleanColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;
        return value.TrimStart('#');
    }

    private sealed class DocumentDto
    {
        public string? Format { get; set; }
        public string? Version { get; set; }
        public PortablePageSetup? Setup { get; set; }
        public List<ParagraphDto>? Paragraphs { get; set; }
    }

    private sealed class ParagraphDto
    {
        public PortableStyle Style { get; set; }
        public PortableAlignment Alignment { get; set; }
        public PortableListKind List { get; set; }
        public string? Text { get; set; }
        public List<RunDto>? Runs { get; set; }
    }

    private sealed class RunDto
    {
        public string? Text { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public string? FontFamily { get; set; }
        public double? FontSizePt { get; set; }
        public string? ColorHex { get; set; }
    }
}
