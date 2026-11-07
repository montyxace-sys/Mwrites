using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace Mwrites.Core.Documents;

public static class PlainTextService
{
    public static void Save(FlowDocument document, string path)
    {
        var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
        File.WriteAllText(path, text, new UTF8Encoding(true));
    }

    public static FlowDocument Load(string path, PageSetup setup)
    {
        var document = DocumentStyles.CreateDocument(setup);
        var bytes = File.ReadAllBytes(path);
        var text = Decode(bytes);
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var count = lines.Length;
        if (count > 1 && lines[^1].Length == 0) count--;

        for (var i = 0; i < count; i++)
        {
            document.Blocks.Add(DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, lines[i]));
        }
        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(DocumentStyles.CreateParagraph(document));
        }
        return document;
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}

public static class RtfService
{
    public static void Save(FlowDocument document, string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        new TextRange(document.ContentStart, document.ContentEnd).Save(stream, DataFormats.Rtf);
    }

    public static FlowDocument Load(string path, PageSetup setup)
    {
        var document = DocumentStyles.CreateDocument(setup);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            new TextRange(document.ContentStart, document.ContentEnd).Load(stream, DataFormats.Rtf);
        }
        Normalize(document);
        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(DocumentStyles.CreateParagraph(document));
        }
        return document;
    }

    private static void Normalize(FlowDocument document)
    {
        var paragraphs = new List<Paragraph>();
        DocumentStats.Collect(document.Blocks, paragraphs);
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Tag is null)
            {
                DocumentStyles.ApplyStyle(document, paragraph, DocumentStyles.Normal);
            }
        }
    }
}

public static class DocumentService
{
    public static readonly string[] OpenExtensions = { ".docx", ".rtf", ".txt" };

    public static FlowDocument Open(string path)
    {
        var setup = PageSetup.A4;
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".docx" => DocxReader.Load(path),
            ".rtf" => RtfService.Load(path, setup),
            ".txt" => PlainTextService.Load(path, setup),
            ".doc" => throw new NotSupportedException(
                "El formato .doc antiguo no está soportado. Ábrelo en Word y guárdalo como .docx."),
            _ => throw new NotSupportedException("Formato de archivo no soportado.")
        };
    }

    public static void Save(FlowDocument document, string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".docx":
                DocxWriter.Save(document, path);
                break;
            case ".rtf":
                RtfService.Save(document, path);
                break;
            case ".txt":
                PlainTextService.Save(document, path);
                break;
            default:
                throw new NotSupportedException("Formato de archivo no soportado.");
        }
    }
}
