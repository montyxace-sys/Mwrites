using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Mwrites.Core.Documents;
using Mwrites.Portable.Core;

namespace Mwrites.Portable.App;

public partial class MainView : UserControl
{
    private static readonly FilePickerFileType DocumentType = new("Documentos")
    {
        Patterns = new[] { "*.docx", "*.txt", "*.mwdoc" }
    };

    private PortableDocument _document = PortableDocument.Sample();
    private string? _path;
    private bool _loading;

    public MainView()
    {
        InitializeComponent();
        Editor.Text = _document.EditorText;
        UpdateStats();
    }

    private void OnNew(object? sender, RoutedEventArgs e)
    {
        _document = new PortableDocument();
        _document.Paragraphs.Add(PortableParagraph.Plain(string.Empty));
        _path = null;
        Editor.Text = _document.EditorText;
        Status("Documento nuevo.");
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is not null && storage.CanOpen)
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Abrir documento",
                AllowMultiple = false,
                FileTypeFilter = new[] { DocumentType }
            });
            if (files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (path is null)
            {
                Status("Este archivo no se puede abrir como ruta local.");
                return;
            }
            OpenPath(path);
            return;
        }

        // En móvil no siempre hay selector de archivos: se usa el documento local de la app.
        var local = LocalDocumentPath();
        if (File.Exists(local)) OpenPath(local);
        else Status("En este dispositivo usa «Guardar» y después «Abrir» para volver al documento local.");
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_path))
        {
            SaveTo(_path!);
            return;
        }
        await SaveAsAsync();
    }

    private async void OnSaveAs(object? sender, RoutedEventArgs e) => await SaveAsAsync();

    private async Task SaveAsAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is not null && storage.CanSave)
        {
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar documento",
                SuggestedFileName = "Documento.docx",
                DefaultExtension = ".docx",
                FileTypeChoices = new[] { DocumentType }
            });
            if (file is null) return;
            var path = file.TryGetLocalPath();
            if (path is null)
            {
                Status("Este destino no permite guardar como archivo local.");
                return;
            }
            SaveTo(path);
            return;
        }
        SaveTo(LocalDocumentPath());
    }

    private void OpenPath(string path)
    {
        try
        {
            _document = PortableDocumentService.Open(path);
            _path = path;
            _loading = true;
            Editor.Text = _document.EditorText;
            _loading = false;
            UpdateStats();
            Status($"Abierto {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                      or NotSupportedException)
        {
            _loading = false;
            Status("No se pudo abrir: " + ex.Message);
        }
    }

    private void SaveTo(string path)
    {
        try
        {
            SyncFromEditor();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            PortableDocumentService.Save(_document, path);
            _path = path;
            Status($"Guardado {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Status("No se pudo guardar: " + ex.Message);
        }
    }

    private void OnUpper(object? sender, RoutedEventArgs e) => ApplyCase(TextCase.Upper);

    private void OnLower(object? sender, RoutedEventArgs e) => ApplyCase(TextCase.Lower);

    private void OnTitle(object? sender, RoutedEventArgs e) => ApplyCase(TextCase.Title);

    private void OnSentence(object? sender, RoutedEventArgs e) => ApplyCase(TextCase.Sentence);

    private void ApplyCase(TextCase mode)
    {
        SyncFromEditor();
        var text = Editor.Text ?? string.Empty;
        var first = CountLines(text, Editor.SelectionStart);
        var last = CountLines(text, Editor.SelectionEnd);
        for (var line = first; line <= last && line < _document.Paragraphs.Count; line++)
            PortableFind.ApplyCase(_document, mode, line);

        _loading = true;
        Editor.Text = _document.EditorText;
        Editor.SelectionStart = Math.Min(Editor.SelectionStart, Editor.Text.Length);
        Editor.SelectionEnd = Editor.SelectionStart;
        _loading = false;
        Status($"Caso «{mode}» aplicado.");
    }

    private static int CountLines(string text, int position)
    {
        var end = Math.Min(position, text.Length);
        var lines = 0;
        for (var i = 0; i < end; i++) if (text[i] == '\n') lines++;
        return lines;
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateStats();
        if (!_loading) Status("Documento modificado. Guarda para conservar los cambios.");
    }

    private void SyncFromEditor()
    {
        var setup = _document.Setup;
        _document = PortableDocumentService.FromEditorText(Editor.Text ?? string.Empty);
        _document.Setup = setup;
    }

    private void UpdateStats()
    {
        var stats = PortableStats.Compute(PortableDocumentService.FromEditorText(Editor.Text ?? string.Empty));
        StatusText.Text = $"{stats.Words} palabras · {stats.Characters} caracteres · {stats.Paragraphs} párrafos · {stats.Headings} títulos";
    }

    private void Status(string message) => StatusText.Text = message;

    private static string LocalDocumentPath()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mwrites");
        return Path.Combine(directory, "documento.docx");
    }
}
