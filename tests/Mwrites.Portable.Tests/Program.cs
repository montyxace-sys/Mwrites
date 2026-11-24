using System.IO.Compression;
using System.Xml.Linq;
using Mwrites.Core.Documents;
using Mwrites.Portable.Core;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static string _workDir = string.Empty;

    private static int Main()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "mwrites-portable-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
        try
        {
            Run("DOCX portable: ida y vuelta", DocxRoundTrip);
            Run("DOCX portable: estructura OOXML", DocxStructure);
            Run("TXT portable: acentos y estilos", TextRoundTrip);
            Run("MWDOC portable: ida y vuelta", JsonRoundTrip);
            Run("Estadísticas portable", Stats);
            Run("Búsqueda y reemplazo portable", FindReplace);
            Run("Mayúsculas y minúsculas portable", Case);
            Run("Archivo no compatible", Unsupported);
        }
        finally
        {
            try { Directory.Delete(_workDir, true); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine($"Resultado: {_passed} correctas, {_failed} fallidas.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"[OK]   {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"[FALLO] {name}");
            Console.WriteLine("       " + ex.Message);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static PortableDocument Sample()
    {
        var document = new PortableDocument { Setup = PortablePageSetup.Letter };
        var title = PortableParagraph.Plain("Informe de prueba", PortableStyle.Heading1);
        title.Runs[0].Bold = true;
        title.Runs[0].FontSizePt = 16;
        document.Paragraphs.Add(title);

        var paragraph = new PortableParagraph { Alignment = PortableAlignment.Center };
        paragraph.Runs.Add(new PortableRun { Text = "Texto " });
        paragraph.Runs.Add(new PortableRun { Text = "negrita", Bold = true, ColorHex = "C00000" });
        paragraph.Runs.Add(new PortableRun { Text = " y cursiva", Italic = true, Underline = true, FontFamily = "Calibri" });
        document.Paragraphs.Add(paragraph);
        document.Paragraphs.Add(PortableParagraph.Plain("Ágil ñandú con acentos."));
        return document;
    }

    private static void DocxRoundTrip()
    {
        var original = Sample();
        var path = Path.Combine(_workDir, "docx-roundtrip.docx");
        PortableDocumentService.Save(original, path);

        var loaded = PortableDocumentService.Open(path);
        Assert(loaded.Paragraphs.Count == original.Paragraphs.Count, "Número de párrafos distinto.");
        Assert(loaded.Paragraphs[0].Text == "Informe de prueba", "Título perdido.");
        Assert(loaded.Paragraphs[0].Style == PortableStyle.Heading1, "Estilo de título perdido.");
        Assert(loaded.Paragraphs[1].Text == "Texto negrita y cursiva", "Texto concatenado distinto.");
        Assert(loaded.Paragraphs[1].Alignment == PortableAlignment.Center, "Alineación perdida.");
        Assert(loaded.Paragraphs[1].Runs[1].Bold, "Negrita perdida.");
        Assert(loaded.Paragraphs[1].Runs[1].ColorHex == "C00000", "Color perdido.");
        Assert(loaded.Paragraphs[1].Runs[2].Italic && loaded.Paragraphs[1].Runs[2].Underline, "Cursiva o subrayado perdidos.");
        Assert(loaded.Paragraphs[2].Text == "Ágil ñandú con acentos.", "Acentos dañados.");
        Assert(loaded.Setup.SizeName == "Carta", "Tamaño de página perdido.");
    }

    private static void DocxStructure()
    {
        var path = Path.Combine(_workDir, "docx-structure.docx");
        PortableDocumentService.Save(Sample(), path);
        using var archive = ZipFile.OpenRead(path);
        Assert(archive.GetEntry("[Content_Types].xml") is not null, "Faltan los tipos de contenido.");
        Assert(archive.GetEntry("_rels/.rels") is not null, "Faltan las relaciones.");
        Assert(archive.GetEntry("word/document.xml") is not null, "Falta document.xml.");
        using var stream = archive.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        Assert(xml.Descendants(w + "p").Count() == 3, "El XML no tiene tres párrafos.");
        Assert(xml.Descendants(w + "sectPr").Count() == 1, "Falta la configuración de página.");
    }

    private static void TextRoundTrip()
    {
        var document = PortableDocumentService.FromEditorText(
            "# Título\nTexto con ñ y á\n- Viñeta\n> Cita");
        Assert(document.Paragraphs[0].Style == PortableStyle.Heading1, "El marcador # no se interpretó.");
        Assert(document.Paragraphs[2].List == PortableListKind.Bullet, "El marcador - no se interpretó.");
        Assert(document.Paragraphs[3].Style == PortableStyle.Quote, "El marcador > no se interpretó.");

        var path = Path.Combine(_workDir, "texto.txt");
        PortableDocumentService.Save(document, path);
        var loaded = PortableDocumentService.Open(path);
        Assert(loaded.EditorText == document.EditorText, "El texto plano no conservó las marcas.");
    }

    private static void JsonRoundTrip()
    {
        var path = Path.Combine(_workDir, "documento.mwdoc");
        var original = Sample();
        PortableDocumentService.Save(original, path);
        var loaded = PortableDocumentService.Open(path);
        Assert(loaded.Paragraphs.Count == 3, "MWDOC perdió párrafos.");
        Assert(loaded.Paragraphs[1].Runs[1].Bold, "MWDOC perdió el formato.");
        Assert(loaded.Setup.WidthPt == original.Setup.WidthPt, "MWDOC perdió la página.");
    }

    private static void Stats()
    {
        var document = PortableDocumentService.FromEditorText("# Capítulo uno\nHola mundo, esto es una prueba.\nTercera línea.");
        var stats = PortableStats.Compute(document);
        Assert(stats.Paragraphs == 3, "Párrafos mal contados.");
        Assert(stats.Headings == 1, "Títulos mal contados.");
        Assert(stats.Words > 8, "Palabras mal contadas.");
    }

    private static void FindReplace()
    {
        var document = PortableDocumentService.FromEditorText("El gato duerme. Otro gato mira.");
        Assert(PortableFind.FindAll(document, "gato").Count == 2, "Coincidencias incorrectas.");
        Assert(PortableFind.ReplaceAll(document, "gato", "perro") == 2, "Reemplazos incorrectos.");
        Assert(document.Paragraphs[0].Text == "El perro duerme. Otro perro mira.", "Reemplazo mal aplicado.");
    }

    private static void Case()
    {
        var document = PortableDocumentService.FromEditorText("hola mundo");
        PortableFind.ApplyCase(document, TextCase.Title);
        Assert(document.Paragraphs[0].Text == "Hola Mundo", "El cambio de caso no se aplicó.");
    }

    private static void Unsupported()
    {
        Assert(!PortableDocumentService.IsSupported("documento.pdf"), "PDF marcado como compatible.");
        try
        {
            PortableDocumentService.Open(Path.Combine(_workDir, "no.docx"));
            throw new InvalidOperationException("No se lanzó el error esperado.");
        }
        catch (FileNotFoundException)
        {
            // Correcto.
        }
    }
}
