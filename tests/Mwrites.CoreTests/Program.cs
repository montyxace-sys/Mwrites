using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mwrites.Core.Documents;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static string _workDir = string.Empty;

    [STAThread]
    private static int Main()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "mwrites-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);

        try
        {
            Run("Estadísticas de documento", StatsCounts);
            Run("Búsqueda y reemplazo", FindAndReplace);
            Run("Búsqueda con mayúsculas y palabra completa", FindOptions);
            Run("DOCX: ida y vuelta completa", DocxRoundTrip);
            Run("DOCX: archivo inválido", DocxInvalid);
            Run("DOCX: estructura OOXML", DocxStructure);
            Run("Texto plano: acentos y saltos", PlainTextRoundTrip);
            Run("RTF: ida y vuelta", RtfRoundTrip);
            Run("Plantillas: contenido y estilos", TemplatesTest);
            Run("Página: orientación y nombre", PageSetupTest);
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

    private static void StatsCounts()
    {
        var document = DocumentStyles.CreateDocument(PageSetup.A4);
        Templates.Add(document, DocumentStyles.Heading1, "Capítulo uno");
        Templates.Add(document, DocumentStyles.Normal, "Hola mundo, esto es una prueba.");
        Templates.Add(document, DocumentStyles.Normal, "Tercera línea con 3 números.");

        var stats = DocumentStats.Compute(document);
        Assert(stats.Words == 13, $"Palabras esperadas 13, obtenidas {stats.Words}.");
        Assert(stats.Paragraphs == 3, $"Párrafos esperados 3, obtenidos {stats.Paragraphs}.");
        Assert(stats.Headings == 1, $"Encabezados esperados 1, obtenidos {stats.Headings}.");
        Assert(stats.CharactersNoSpaces > 20, "No se contaron los caracteres sin espacios.");
    }

    private static void FindAndReplace()
    {
        var document = DocumentStyles.CreateDocument(PageSetup.A4);
        var paragraph = DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, "El gato duerme. Otro gato mira.");
        paragraph.Inlines.Add(new Run(" Gato con mayúscula."));
        document.Blocks.Add(paragraph);

        var matches = FindEngine.FindAll(document, "gato", new FindOptions());
        Assert(matches.Count == 3, $"Coincidencias esperadas 3, obtenidas {matches.Count}.");
        var replaced = FindEngine.ReplaceAll(document, "gato", "perro", new FindOptions());
        Assert(replaced == 3, $"Reemplazos esperados 3, obtenidos {replaced}.");
        var text = DocumentStats.GetPlainText(document);
        Assert(text.Contains("perro"), "No se escribió el reemplazo.");
        Assert(!text.Contains("gato", StringComparison.OrdinalIgnoreCase), "Quedaron coincidencias sin reemplazar.");
    }

    private static void FindOptions()
    {
        var document = DocumentStyles.CreateDocument(PageSetup.A4);
        document.Blocks.Add(DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, "Casa casada Casa."));
        Assert(FindEngine.FindAll(document, "casa", new FindOptions()).Count == 3, "Sin distinguir mayúsculas deben ser 3.");
        Assert(FindEngine.FindAll(document, "Casa", new FindOptions(MatchCase: true)).Count == 2, "Con mayúsculas deben ser 2.");
        Assert(FindEngine.FindAll(document, "casa", new FindOptions(WholeWord: true)).Count == 2, "Palabra completa deben ser 2.");
        Assert(FindEngine.FindAll(document, "Casa", new FindOptions(true, true)).Count == 2, "Casa exacta deben ser 2.");
        Assert(FindEngine.FindAll(document, "casa", new FindOptions(true, true)).Count == 0, "casa exacta en minúscula deben ser 0.");
    }

    private static void DocxRoundTrip()
    {
        var setup = PageSetup.Carta.WithOrientation(true);
        var document = DocumentStyles.CreateDocument(setup);

        var title = DocumentStyles.CreateParagraph(document, DocumentStyles.Heading1, "Informe de prueba");
        document.Blocks.Add(title);

        var paragraph = DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, "Texto normal con ");
        paragraph.Inlines.Add(new Run("negrita") { FontWeight = FontWeights.Bold });
        paragraph.Inlines.Add(new Run(" y "));
        paragraph.Inlines.Add(new Run("cursiva") { FontStyle = FontStyles.Italic });
        paragraph.Inlines.Add(new Run(" y "));
        paragraph.Inlines.Add(new Run("subrayado") { TextDecorations = TextDecorations.Underline });
        paragraph.Inlines.Add(new Run(" y color") { Foreground = DocumentStyles.Frozen("#C00000") });
        document.Blocks.Add(paragraph);

        var list = new List { MarkerStyle = TextMarkerStyle.Disc };
        list.ListItems.Add(new ListItem(DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, "Primer punto")));
        list.ListItems.Add(new ListItem(DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, "Segundo punto")));
        document.Blocks.Add(list);

        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn { Width = new GridLength(200) });
        table.Columns.Add(new TableColumn { Width = new GridLength(200) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var row1 = new TableRow();
        row1.Cells.Add(new TableCell(DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, "A1")));
        row1.Cells.Add(new TableCell(DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, "B1")));
        var row2 = new TableRow();
        row2.Cells.Add(new TableCell(DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, "A2")));
        row2.Cells.Add(new TableCell(DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, "B2")));
        group.Rows.Add(row1);
        group.Rows.Add(row2);
        document.Blocks.Add(table);

        var image = CreateImage();
        var imageParagraph = DocumentStyles.CreateParagraph(document);
        imageParagraph.Inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center });
        document.Blocks.Add(imageParagraph);

        var path = Path.Combine(_workDir, "prueba.docx");
        DocxWriter.Save(document, path);
        Assert(File.Exists(path), "No se creó el archivo .docx.");

        var loaded = DocxReader.Load(path);
        var text = DocumentStats.GetPlainText(loaded);
        Assert(text.Contains("Informe de prueba"), "Falta el encabezado.");
        Assert(text.Contains("negrita") && text.Contains("cursiva"), "Faltan los textos con formato.");
        Assert(text.Contains("Primer punto") && text.Contains("Segundo punto"), "Faltan los elementos de la lista.");
        Assert(text.Contains("A1") && text.Contains("B2"), "Faltan las celdas de la tabla.");

        var paragraphs = new List<Paragraph>();
        DocumentStats.Collect(loaded.Blocks, paragraphs);
        Assert(paragraphs.Any(p => DocumentStyles.StyleKeyOf(p) == DocumentStyles.Heading1), "No se conservó el estilo del encabezado.");
        Assert(paragraphs.Any(p => p.Inlines.OfType<Run>().Any(r => r.FontWeight == FontWeights.Bold)), "No se conservó la negrita.");
        Assert(paragraphs.Any(p => p.Inlines.OfType<Run>().Any(r => r.FontStyle == FontStyles.Italic)), "No se conservó la cursiva.");
        Assert(paragraphs.Any(p => p.Inlines.OfType<InlineUIContainer>().Any(c => c.Child is Image)), "No se conservó la imagen.");
        Assert(loaded.Blocks.OfType<List>().Any(l => l.ListItems.Count == 2), "No se conservó la lista.");
        Assert(loaded.Blocks.OfType<Table>().Any(t => t.RowGroups[0].Rows.Count == 2), "No se conservó la tabla.");

        var loadedSetup = loaded.Tag as PageSetup;
        Assert(loadedSetup is not null, "No se conservó la configuración de página.");
        Assert(Math.Abs(loadedSetup!.Width - setup.Width) < 2 && Math.Abs(loadedSetup.Height - setup.Height) < 2,
            "Las dimensiones de página no coinciden.");
        Assert(loadedSetup.Landscape, "Se perdió la orientación horizontal.");
    }

    private static void DocxInvalid()
    {
        var path = Path.Combine(_workDir, "malo.docx");
        File.WriteAllText(path, "no es un docx");
        var threw = false;
        try
        {
            DocxReader.Load(path);
        }
        catch (InvalidDataException)
        {
            threw = true;
        }
        Assert(threw, "Un archivo que no es DOCX debe rechazarse.");
    }

    private static void DocxStructure()
    {
        var path = Path.Combine(_workDir, "estructura.docx");
        var document = DocumentStyles.CreateDocument(PageSetup.A4);
        document.Blocks.Add(DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, "Contenido"));
        var list = new List { MarkerStyle = TextMarkerStyle.Decimal };
        list.ListItems.Add(new ListItem(DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, "Uno")));
        document.Blocks.Add(list);
        DocxWriter.Save(document, path);

        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in new[] { "[Content_Types].xml", "_rels/.rels", "word/document.xml", "word/styles.xml", "word/numbering.xml", "word/_rels/document.xml.rels" })
        {
            Assert(archive.GetEntry(entry) is not null, $"Falta la parte {entry}.");
        }
        var documentXml = ReadEntry(archive, "word/document.xml");
        Assert(documentXml.Contains("<w:numPr>"), "La lista no generó numeración.");
        Assert(documentXml.Contains("w:document"), "El XML del documento no es válido.");
    }

    private static void PlainTextRoundTrip()
    {
        var document = DocumentStyles.CreateDocument(PageSetup.A4);
        Templates.Add(document, DocumentStyles.Normal, "Año: 2026, acción y corazón.");
        Templates.Add(document, DocumentStyles.Normal, "Segunda línea con emoji 🙂.");
        var path = Path.Combine(_workDir, "texto.txt");
        PlainTextService.Save(document, path);
        var loaded = PlainTextService.Load(path, PageSetup.A4);
        var text = DocumentStats.GetPlainText(loaded);
        Assert(text.Contains("corazón") && text.Contains("🙂"), "Se perdieron los caracteres especiales.");
        Assert(loaded.Blocks.Count == 2, "No se conservaron las dos líneas.");
    }

    private static void RtfRoundTrip()
    {
        var document = DocumentStyles.CreateDocument(PageSetup.A4);
        var paragraph = DocumentStyles.CreateParagraph(document, DocumentStyles.Normal, "Hola ");
        paragraph.Inlines.Add(new Run("RTF") { FontWeight = FontWeights.Bold });
        document.Blocks.Add(paragraph);
        var path = Path.Combine(_workDir, "prueba.rtf");
        RtfService.Save(document, path);
        var loaded = RtfService.Load(path, PageSetup.A4);
        Assert(DocumentStats.GetPlainText(loaded).Contains("Hola RTF"), "Se perdió el texto al pasar por RTF.");
    }

    private static void TemplatesTest()
    {
        foreach (var template in Templates.All)
        {
            var document = template.Create(PageSetup.A4);
            Assert(document.Blocks.Count > 0, $"La plantilla {template.Name} está vacía.");
        }
        var report = Templates.Report(PageSetup.A4);
        var paragraphs = new List<Paragraph>();
        DocumentStats.Collect(report.Blocks, paragraphs);
        Assert(paragraphs.Any(p => DocumentStyles.StyleKeyOf(p) == DocumentStyles.Heading1), "El informe no tiene encabezados.");
        Assert(report.Blocks.OfType<List>().Any(), "El informe no tiene listas.");
    }

    private static void PageSetupTest()
    {
        var landscape = PageSetup.A4.WithOrientation(true);
        Assert(landscape.Landscape, "No se cambió la orientación.");
        Assert(Math.Abs(landscape.Width - PageSetup.A4.Height) < 0.01, "No se intercambiaron las dimensiones.");
        var named = PageSetup.FromTwips(11906, 16838, 1440, 1440, 1440, 1440);
        Assert(named.SizeName == "A4", $"Se esperaba A4 y se obtuvo {named.SizeName}.");
    }

    private static Image CreateImage()
    {
        var pixels = new byte[16 * 16 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x2B;
            pixels[i + 1] = 0x57;
            pixels[i + 2] = 0x9A;
            pixels[i + 3] = 0xFF;
        }
        var source = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 16 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(memory.ToArray());
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return new Image
        {
            Source = bitmap,
            Width = 120,
            Height = 120,
            Stretch = Stretch.Uniform,
            Tag = memory.ToArray()
        };
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        if (entry is null) return string.Empty;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
