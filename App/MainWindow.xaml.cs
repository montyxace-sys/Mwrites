using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mwrites.Core.Documents;
using Mwrites.Dialogs;
using Microsoft.Win32;

namespace Mwrites;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _headingsTimer;
    private readonly DispatcherTimer _statsTimer;
    private FlowDocument _document = null!;
    private PageSetup _setup = PageSetup.A4;
    private string? _path;
    private bool _dirty;
    private bool _ready;
    private bool _loadingDocument;
    private bool _syncingToolbar;
    private bool _syncingZoom;
    private bool _suppressGallery;
    private bool _suppressHeadings;
    private bool _suppressBackstage;
    private bool _suppressRecent;
    private bool _suppressList;
    private bool _navVisible = true;
    private double _navWidth = 248;
    private bool _focusMode;
    private bool _readingMode;
    private int _pageCount = 1;
    private double _zoomPercent = 100;

    public MainWindow()
    {
        InitializeComponent();

        _headingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _headingsTimer.Tick += (_, _) =>
        {
            _headingsTimer.Stop();
            RefreshHeadings();
        };
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _statsTimer.Tick += (_, _) =>
        {
            _statsTimer.Stop();
            UpdateStats();
            UpdatePageCount();
        };

        _loadingDocument = true;
        _setup = PageSetup.A4;
        _document = Templates.Blank(_setup);
        Editor.Document = _document;
        FillCombos();
        BuildTemplateCards();
        _loadingDocument = false;

        ApplyPageSetupToUi();
        UpdateRecentList();
        UpdateTitle();
        UpdateStats();
        RefreshHeadings();
        InitializeLanguage();

        RulerCanvas.SizeChanged += (_, _) => UpdateRuler();
        Editor.SizeChanged += (_, _) => UpdatePageBreakOverlaySoon();
        SetZoom(100, true);
        UpdateInfo();

        _ready = true;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Editor.Focus();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdatePageCount));
        UpdatePageBreakOverlaySoon();
    }

    // ================= ARCHIVO =================

    private void OnNewCommand(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSaveIfDirty()) return;
        LoadDocument(Templates.Blank(PageSetup.A4), null);
        HideBackstage();
    }

    private void OnOpenCommand(object sender, RoutedEventArgs e) => OpenFileDialogInteractive();

    private void OnSaveCommand(object sender, RoutedEventArgs e) => SaveDocument();

    private void OnSaveAsCommand(object sender, RoutedEventArgs e) => SaveDocumentAs();

    private void OnPrintCommand(object sender, RoutedEventArgs e) => PrintDocument();

    private bool SaveDocument()
    {
        if (_path is null) return SaveDocumentAs();
        try
        {
            DocumentService.Save(_document, _path);
            RecentFiles.Add(_path);
            _dirty = false;
            UpdateTitle();
            UpdateInfo();
            UpdateRecentList();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(this, $"No se pudo guardar el documento.\n\n{ex.Message}", "Mwrites",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private bool SaveDocumentAs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar documento",
            Filter = "Documento de Word (*.docx)|*.docx|Texto enriquecido (*.rtf)|*.rtf|Texto sin formato (*.txt)|*.txt",
            DefaultExt = ".docx",
            AddExtension = true,
            FileName = _path is null ? DocumentName : System.IO.Path.GetFileName(_path),
            InitialDirectory = _path is not null ? System.IO.Path.GetDirectoryName(_path) : null
        };
        if (dialog.ShowDialog(this) != true) return false;

        try
        {
            DocumentService.Save(_document, dialog.FileName);
            _path = dialog.FileName;
            RecentFiles.Add(_path);
            _dirty = false;
            UpdateTitle();
            UpdateInfo();
            UpdateRecentList();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(this, $"No se pudo guardar el documento.\n\n{ex.Message}", "Mwrites",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void OpenFileDialogInteractive()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Abrir documento",
            Filter = "Documentos compatibles (*.docx;*.rtf;*.txt)|*.docx;*.rtf;*.txt|Documento de Word (*.docx)|*.docx|Texto enriquecido (*.rtf)|*.rtf|Texto sin formato (*.txt)|*.txt"
        };
        if (dialog.ShowDialog(this) == true)
        {
            if (!ConfirmSaveIfDirty()) return;
            OpenPath(dialog.FileName);
        }
    }

    private void OpenPath(string path)
    {
        try
        {
            var document = DocumentService.Open(path);
            LoadDocument(document, path);
            RecentFiles.Add(path);
            UpdateRecentList();
            HideBackstage();
            Editor.Focus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        {
            MessageBox.Show(this, $"No se pudo abrir el documento.\n\n{ex.Message}", "Mwrites",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadDocument(FlowDocument document, string? path)
    {
        _loadingDocument = true;
        _document = document;
        Editor.Document = document;
        _setup = document.Tag as PageSetup ?? PageSetup.A4;
        _path = path;
        _dirty = false;
        ApplyPageSetupToUi();
        Editor.CaretPosition = document.ContentStart;
        _loadingDocument = false;

        UpdateTitle();
        UpdateStats();
        RefreshHeadings();
        UpdateInfo();
        UpdatePageCount();
        UpdateToolbarState();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdatePageCount));
        UpdatePageBreakOverlaySoon();
    }

    private bool ConfirmSaveIfDirty()
    {
        if (!_dirty) return true;
        var result = MessageBox.Show(this,
            $"¿Quieres guardar los cambios en «{DocumentName}»?",
            "Mwrites", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => SaveDocument(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmSaveIfDirty()) e.Cancel = true;
    }

    private string DocumentName =>
        _path is null ? "Documento sin título" : System.IO.Path.GetFileNameWithoutExtension(_path);

    private void MarkDirty()
    {
        _dirty = true;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var suffix = _dirty ? " *" : string.Empty;
        Title = $"{DocumentName}{suffix} - Mwrites";
        BackstageDocName.Text = DocumentName;
    }

    private void UpdateInfo()
    {
        InfoLocation.Text = _path ?? "Sin guardar";
        if (_path is not null && File.Exists(_path))
        {
            var size = new FileInfo(_path).Length;
            InfoSize.Text = size < 1024 * 1024
                ? $"{size / 1024.0:0.0} KB"
                : $"{size / (1024.0 * 1024.0):0.0} MB";
        }
        else
        {
            InfoSize.Text = "—";
        }
        InfoPages.Text = _pageCount.ToString();
        InfoWords.Text = DocumentStats.Compute(_document).Words.ToString("N0");
        var marginCm = _setup.MarginLeft / (96.0 / 2.54);
        InfoPageSetup.Text = $"{_setup.SizeName} · {(_setup.Landscape ? "Horizontal" : "Vertical")} · Márgenes {marginCm:0.##} cm";
    }

    // ================= PORTAPAPELES Y FORMATO =================

    private void OnPaste(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsImage() && !Clipboard.ContainsText())
        {
            var image = Clipboard.GetImage();
            if (image is not null)
            {
                InsertImageElement(image, null);
                return;
            }
        }
        Editor.Paste();
    }

    private void OnCut(object sender, RoutedEventArgs e) => Editor.Cut();

    private void OnCopy(object sender, RoutedEventArgs e) => Editor.Copy();

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        Editor.SelectAll();
        Editor.Focus();
    }

    // TextSelection (not a plain TextRange) so formatting also sticks to the caret
    // when nothing is selected and applies to the text typed afterwards.
    private TextRange SelectionRange() => Editor.Selection;

    private void OnFontFamilyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _syncingToolbar) return;
        if (FontFamilyCombo.SelectedItem is not string name) return;
        SelectionRange().ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(name));
        MarkDirty();
        Editor.Focus();
    }

    private void OnFontSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _syncingToolbar) return;
        if (FontSizeCombo.SelectedItem is not double points) return;
        SelectionRange().ApplyPropertyValue(TextElement.FontSizeProperty, points * 96.0 / 72.0);
        MarkDirty();
        Editor.Focus();
    }

    private void OnGrowFont(object sender, RoutedEventArgs e) => ChangeFontSize(2);

    private void OnShrinkFont(object sender, RoutedEventArgs e) => ChangeFontSize(-2);

    private void ChangeFontSize(double delta)
    {
        var current = GetPropertyValue(TextElement.FontSizeProperty);
        var size = current is double value ? value * 72.0 / 96.0 : 11;
        var newSize = Math.Clamp(Math.Round(size + delta), 6, 96);
        SelectionRange().ApplyPropertyValue(TextElement.FontSizeProperty, newSize * 96.0 / 72.0);
        MarkDirty();
        SelectFontSizeItem(newSize);
        Editor.Focus();
    }

    private void OnBold(object sender, RoutedEventArgs e)
    {
        SelectionRange().ApplyPropertyValue(TextElement.FontWeightProperty,
            BoldToggle.IsChecked == true ? FontWeights.Bold : FontWeights.Normal);
        MarkDirty();
        Editor.Focus();
    }

    private void OnItalic(object sender, RoutedEventArgs e)
    {
        SelectionRange().ApplyPropertyValue(TextElement.FontStyleProperty,
            ItalicToggle.IsChecked == true ? FontStyles.Italic : FontStyles.Normal);
        MarkDirty();
        Editor.Focus();
    }

    private void OnUnderline(object sender, RoutedEventArgs e) => ToggleDecoration(TextDecorationLocation.Underline);

    private void OnStrike(object sender, RoutedEventArgs e) => ToggleDecoration(TextDecorationLocation.Strikethrough);

    private void ToggleDecoration(TextDecorationLocation location)
    {
        var current = GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        var enabled = current?.Any(d => d.Location == location) == true;
        var decorations = new TextDecorationCollection();
        if (current is not null)
        {
            foreach (var decoration in current)
            {
                if (decoration.Location != location) decorations.Add(decoration);
            }
        }
        if (!enabled)
        {
            decorations.Add(location == TextDecorationLocation.Underline
                ? TextDecorations.Underline[0]
                : TextDecorations.Strikethrough[0]);
        }
        decorations.Freeze();
        SelectionRange().ApplyPropertyValue(Inline.TextDecorationsProperty, decorations);
        MarkDirty();
        Editor.Focus();
    }

    private void OnSubscript(object sender, RoutedEventArgs e) => ToggleBaseline(BaselineAlignment.Subscript);

    private void OnSuperscript(object sender, RoutedEventArgs e) => ToggleBaseline(BaselineAlignment.Superscript);

    private void ToggleBaseline(BaselineAlignment alignment)
    {
        var current = GetPropertyValue(Inline.BaselineAlignmentProperty);
        var enabled = current is BaselineAlignment value && value == alignment;
        SelectionRange().ApplyPropertyValue(Inline.BaselineAlignmentProperty,
            enabled ? BaselineAlignment.Baseline : alignment);
        MarkDirty();
        Editor.Focus();
    }

    private void OnFontColor(object sender, RoutedEventArgs e)
    {
        var colors = new (string Name, string Hex)[]
        {
            ("Automático", "#000000"),
            ("Rojo", "#C00000"),
            ("Azul", "#2B579A"),
            ("Verde", "#2E7D32"),
            ("Naranja", "#C55A11"),
            ("Morado", "#7030A0"),
            ("Gris", "#595959")
        };
        ShowColorMenu(sender, colors, hex =>
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            SelectionRange().ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            FontColorSwatch.Background = brush;
            MarkDirty();
            Editor.Focus();
        });
    }

    private void OnHighlight(object sender, RoutedEventArgs e)
    {
        var colors = new (string Name, string Hex)[]
        {
            ("Sin color", "Transparent"),
            ("Amarillo", "#FFF176"),
            ("Verde claro", "#C5E1A5"),
            ("Turquesa", "#80DEEA"),
            ("Azul claro", "#90CAF9"),
            ("Rosa", "#F8BBD0"),
            ("Naranja claro", "#FFCC80")
        };
        ShowColorMenu(sender, colors, hex =>
        {
            Brush brush = hex == "Transparent"
                ? Brushes.Transparent
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            SelectionRange().ApplyPropertyValue(TextElement.BackgroundProperty, brush);
            MarkDirty();
            Editor.Focus();
        });
    }

    private void ShowColorMenu(object sender, (string Name, string Hex)[] colors, Action<string> apply)
    {
        var menu = new ContextMenu();
        foreach (var (name, hex) in colors)
        {
            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(2),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5),
                Margin = new Thickness(0, 0, 8, 0)
            };
            if (hex != "Transparent")
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                swatch.Background = brush;
            }
            else
            {
                swatch.Background = Brushes.White;
            }

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(swatch);
            panel.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });

            var item = new MenuItem { Header = panel };
            var captured = hex;
            item.Click += (_, _) => apply(captured);
            menu.Items.Add(item);
        }

        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
    }

    private void OnClearFormatting(object sender, RoutedEventArgs e)
    {
        Editor.Selection.ClearAllProperties();
        MarkDirty();
        Editor.Focus();
    }

    private void OnClearAllFormatting(object sender, RoutedEventArgs e)
    {
        var range = new TextRange(_document.ContentStart, _document.ContentEnd);
        range.ClearAllProperties();
        DocumentStyles.RefreshStyles(_document);
        MarkDirty();
        UpdateToolbarState();
    }

    private object? GetPropertyValue(DependencyProperty property)
    {
        var value = Editor.Selection.GetPropertyValue(property);
        return value == DependencyProperty.UnsetValue ? null : value;
    }

    // ================= PÁRRAFO =================

    private Paragraph? CaretParagraph
    {
        get
        {
            var paragraph = Editor.CaretPosition.Paragraph;
            if (paragraph is not null) return paragraph;
            var all = new List<Paragraph>();
            DocumentStats.Collect(_document.Blocks, all);
            return all.LastOrDefault();
        }
    }

    private List<Paragraph> SelectedParagraphs()
    {
        var all = new List<Paragraph>();
        DocumentStats.Collect(_document.Blocks, all);
        var start = Editor.Selection.Start;
        var end = Editor.Selection.End;
        var selected = all
            .Where(p => p.ContentEnd.CompareTo(start) >= 0 && p.ContentStart.CompareTo(end) <= 0)
            .ToList();
        if (selected.Count == 0 && CaretParagraph is { } caret) selected.Add(caret);
        return selected;
    }

    private void OnAlignLeft(object sender, RoutedEventArgs e) => SetAlignment(TextAlignment.Left);

    private void OnAlignCenter(object sender, RoutedEventArgs e) => SetAlignment(TextAlignment.Center);

    private void OnAlignRight(object sender, RoutedEventArgs e) => SetAlignment(TextAlignment.Right);

    private void OnAlignJustify(object sender, RoutedEventArgs e) => SetAlignment(TextAlignment.Justify);

    private void SetAlignment(TextAlignment alignment)
    {
        foreach (var paragraph in SelectedParagraphs())
        {
            paragraph.TextAlignment = alignment;
        }
        MarkDirty();
        Editor.Focus();
    }

    private void OnIncreaseIndent(object sender, RoutedEventArgs e) => ChangeIndent(36);

    private void OnDecreaseIndent(object sender, RoutedEventArgs e) => ChangeIndent(-36);

    private void ChangeIndent(double delta)
    {
        foreach (var paragraph in SelectedParagraphs())
        {
            var left = Math.Max(0, paragraph.Margin.Left + delta);
            paragraph.Margin = new Thickness(left, paragraph.Margin.Top, paragraph.Margin.Right, paragraph.Margin.Bottom);
        }
        MarkDirty();
        Editor.Focus();
    }

    private void OnLineSpacingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _suppressList) return;
        if (LineSpacingCombo.SelectedItem is not ComboBoxItem { Tag: double factor }) return;
        foreach (var paragraph in SelectedParagraphs())
        {
            var size = (double)paragraph.GetValue(TextElement.FontSizeProperty);
            paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            paragraph.LineHeight = Math.Round(size * factor * 1.2, 1);
        }
        MarkDirty();
        Editor.Focus();
    }

    private void OnParagraphSpacing(object sender, RoutedEventArgs e)
    {
        var paragraph = CaretParagraph;
        if (paragraph is null) return;
        var before = Math.Round(paragraph.Margin.Top / (96.0 / 72.0), 1);
        var after = Math.Round(paragraph.Margin.Bottom / (96.0 / 72.0), 1);

        var dialog = new SimpleInputDialog("Espaciado del párrafo",
            $"Espacio antes y después del párrafo, en puntos.\n\nAntes: {before:0.#} pt   Después: {after:0.#} pt",
            $"{before:0.#};{after:0.#}") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var parts = dialog.Value.Split(';', ',');
        if (parts.Length != 2 ||
            !double.TryParse(parts[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var beforePt) ||
            !double.TryParse(parts[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var afterPt))
        {
            return;
        }

        paragraph.Margin = new Thickness(paragraph.Margin.Left, beforePt * 96.0 / 72.0,
            paragraph.Margin.Right, afterPt * 96.0 / 72.0);
        MarkDirty();
        Editor.Focus();
    }

    private void OnBullets(object sender, RoutedEventArgs e) => ToggleList(TextMarkerStyle.Disc);

    private void OnNumbering(object sender, RoutedEventArgs e) => ToggleList(TextMarkerStyle.Decimal);

    private void ToggleList(TextMarkerStyle marker)
    {
        var paragraphs = SelectedParagraphs().Where(p => p.SiblingBlocks is not null).ToList();
        if (paragraphs.Count == 0) return;

        var insideLists = paragraphs.Where(p => p.Parent is ListItem { Parent: List }).ToList();
        if (insideLists.Count > 0)
        {
            var lists = insideLists
                .Select(p => ((ListItem)p.Parent).Parent)
                .OfType<List>()
                .Distinct()
                .ToList();
            foreach (var list in lists)
            {
                var collection = list.SiblingBlocks;
                if (collection is null) continue;
                foreach (var item in list.ListItems.ToList())
                {
                    foreach (var block in item.Blocks.ToList())
                    {
                        item.Blocks.Remove(block);
                        collection.InsertBefore(list, block);
                    }
                }
                collection.Remove(list);
            }
        }
        else
        {
            var candidates = paragraphs.Where(p => p.Parent is not ListItem).ToList();
            if (candidates.Count == 0) return;
            var first = candidates[0];
            var collection = first.SiblingBlocks!;
            var list = new List { MarkerStyle = marker, Margin = new Thickness(0, 4, 0, 6) };
            collection.InsertBefore(first, list);
            foreach (var paragraph in candidates)
            {
                paragraph.SiblingBlocks?.Remove(paragraph);
                list.ListItems.Add(new ListItem(paragraph));
            }
            Editor.CaretPosition = candidates[0].ContentStart;
        }

        MarkDirty();
        Editor.Focus();
    }

    private void OnStyleGalleryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _suppressGallery) return;
        if (StyleGallery.SelectedItem is not ListBoxItem { Tag: string key }) return;
        ApplyParagraphStyle(key);
        Editor.Focus();
    }

    // Clicking the style that is already selected raises no SelectionChanged, so without
    // this the gallery keeps focus and anything typed next is swallowed.
    private void OnStyleGalleryMouseUp(object sender, MouseButtonEventArgs e)
    {
        Editor.Focus();
    }

    private void ApplyParagraphStyle(string key)
    {
        var paragraphs = SelectedParagraphs();
        if (paragraphs.Count == 0) return;
        foreach (var paragraph in paragraphs)
        {
            DocumentStyles.ApplyStyle(_document, paragraph, key);
        }
        MarkDirty();
        RefreshHeadingsSoon();
    }

    // ================= INSERTAR =================

    private void OnPageBreak(object sender, RoutedEventArgs e)
    {
        var paragraph = CaretParagraph;
        if (paragraph is null) return;

        if (string.IsNullOrWhiteSpace(DocumentStats.TextOf(paragraph)))
        {
            paragraph.BreakPageBefore = true;
            Editor.CaretPosition = paragraph.ContentStart;
        }
        else
        {
            var next = new Paragraph { BreakPageBefore = true };
            DocumentStyles.ApplyStyle(_document, next, DocumentStyles.Normal);
            paragraph.SiblingBlocks?.InsertAfter(paragraph, next);
            Editor.CaretPosition = next.ContentStart;
        }
        MarkDirty();
        UpdatePageBreakOverlaySoon();
        Editor.Focus();
    }

    // The editable view lays out the document as one continuous sheet, so the paginator's
    // BreakPageBefore has no visible effect while editing. Draw a Word-style marker instead.
    private void UpdatePageBreakOverlaySoon()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdatePageBreakOverlay));
    }

    private void UpdatePageBreakOverlay()
    {
        PageBreakOverlay.Children.Clear();
        if (!_ready) return;

        var paragraphs = new List<Paragraph>();
        DocumentStats.Collect(_document.Blocks, paragraphs);
        if (paragraphs.TrueForAll(p => !p.BreakPageBefore)) return;

        var origin = Editor.TranslatePoint(new Point(0, 0), PageBreakOverlay);

        var brush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA6, 0xB6));
        brush.Freeze();
        var left = origin.X + _setup.MarginLeft;

        foreach (var paragraph in paragraphs)
        {
            if (!paragraph.BreakPageBefore) continue;

            // A paragraph with BreakPageBefore reports its ContentStart on the previous line,
            // so measure from the first real character instead.
            var anchor = paragraph.Inlines.FirstInline is { } inline ? inline.ContentStart : paragraph.ContentStart;
            var rect = anchor.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty) continue;

            var y = origin.Y + rect.Top;
            var line = new System.Windows.Shapes.Line
            {
                X1 = left,
                X2 = left + _setup.ContentWidth,
                Y1 = y,
                Y2 = y,
                Stroke = brush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            };
            PageBreakOverlay.Children.Add(line);

            var label = new TextBlock
            {
                Text = "Salto de página",
                FontSize = 10,
                Foreground = brush
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, origin.X + 4);
            Canvas.SetTop(label, y - label.DesiredSize.Height - 1);
            PageBreakOverlay.Children.Add(label);
        }
    }

    private void OnInsertTable(object sender, RoutedEventArgs e)
    {
        var dialog = new InsertTableDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 6, 0, 10)
        };
        var columnWidth = Math.Max(30, _setup.ContentWidth / dialog.Columns);
        for (var c = 0; c < dialog.Columns; c++)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(columnWidth) });
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        Paragraph? firstCell = null;

        for (var r = 0; r < dialog.Rows; r++)
        {
            var row = new TableRow();
            group.Rows.Add(row);
            for (var c = 0; c < dialog.Columns; c++)
            {
                var paragraph = DocumentStyles.CreateParagraph(_document, DocumentStyles.Normal, string.Empty);
                var cell = new TableCell(paragraph)
                {
                    Padding = new Thickness(7, 4, 7, 4),
                    BorderBrush = DocumentStyles.Frozen("#C9CFD8"),
                    BorderThickness = new Thickness(0.8)
                };
                if (r == 0)
                {
                    cell.Background = DocumentStyles.Frozen("#EDF2F9");
                }
                firstCell ??= paragraph;
                row.Cells.Add(cell);
            }
        }

        var caret = CaretParagraph;
        var collection = caret?.SiblingBlocks ?? _document.Blocks;
        var trailing = DocumentStyles.CreateParagraph(_document, DocumentStyles.Normal);
        if (caret is not null)
        {
            collection.InsertAfter(caret, trailing);
            collection.InsertAfter(caret, table);
        }
        else
        {
            collection.Add(table);
            collection.Add(trailing);
        }

        Editor.CaretPosition = firstCell?.ContentStart ?? trailing.ContentStart;
        MarkDirty();
        Editor.Focus();
    }

    // ================= EDICIÓN DE TABLAS =================

    private static T? FindAncestor<T>(TextPointer? position) where T : DependencyObject
    {
        for (var node = position?.Parent; node is not null; node = node is TextElement element ? element.Parent : null)
        {
            if (node is T match) return match;
        }
        return null;
    }

    private (TableCell Cell, TableRow Row, Table Table)? TableContext()
    {
        if (FindAncestor<TableCell>(Editor.CaretPosition) is not { } cell) return null;
        if (FindAncestor<TableRow>(Editor.CaretPosition) is not { } row) return null;
        if (FindAncestor<Table>(Editor.CaretPosition) is not { } table) return null;
        return (cell, row, table);
    }

    private TableCell CreateCellLike(TableCell template, bool copyBackground = false)
    {
        var paragraph = DocumentStyles.CreateParagraph(_document, DocumentStyles.Normal, string.Empty);
        var cell = new TableCell(paragraph)
        {
            Padding = template.Padding,
            BorderBrush = template.BorderBrush,
            BorderThickness = template.BorderThickness
        };
        if (copyBackground) cell.Background = template.Background;
        return cell;
    }

    private void OnAddRowBelow(object sender, RoutedEventArgs e)
    {
        if (TableContext() is not { } context) return;
        if (context.Row.Parent is not TableRowGroup group) return;

        var row = new TableRow();
        foreach (var _ in context.Row.Cells)
        {
            row.Cells.Add(CreateCellLike(context.Cell));
        }

        group.Rows.Insert(group.Rows.IndexOf(context.Row) + 1, row);
        Editor.CaretPosition = row.Cells[0].Blocks.FirstBlock!.ContentStart;
        TableEditPanel.Visibility = Visibility.Visible;
        MarkDirty();
        Editor.Focus();
    }

    private void OnRemoveRow(object sender, RoutedEventArgs e)
    {
        if (TableContext() is not { } context) return;
        if (context.Row.Parent is not TableRowGroup group || group.Rows.Count <= 1) return;

        var next = Math.Min(group.Rows.IndexOf(context.Row) + 1, group.Rows.Count - 1);
        group.Rows.Remove(context.Row);
        var target = group.Rows[Math.Min(next, group.Rows.Count - 1)];
        if (target.Cells.Count > 0) Editor.CaretPosition = target.Cells[0].Blocks.FirstBlock!.ContentStart;
        MarkDirty();
        Editor.Focus();
    }

    private void OnAddColumnRight(object sender, RoutedEventArgs e)
    {
        if (TableContext() is not { } context) return;
        var columnIndex = context.Row.Cells.IndexOf(context.Cell);

        foreach (var group in context.Table.RowGroups)
        foreach (var row in group.Rows)
        {
            var insertAt = Math.Min(columnIndex + 1, row.Cells.Count);
            var template = row.Cells[Math.Min(columnIndex, row.Cells.Count - 1)];
            row.Cells.Insert(insertAt, CreateCellLike(template, copyBackground: true));
        }

        var insertColumnAt = Math.Min(columnIndex + 1, context.Table.Columns.Count);
        context.Table.Columns.Insert(insertColumnAt, new TableColumn());
        RebalanceColumns(context.Table);

        var target = context.Row.Cells[Math.Min(columnIndex + 1, context.Row.Cells.Count - 1)];
        Editor.CaretPosition = target.Blocks.FirstBlock!.ContentStart;
        TableEditPanel.Visibility = Visibility.Visible;
        MarkDirty();
        Editor.Focus();
    }

    private void RebalanceColumns(Table table)
    {
        if (table.Columns.Count == 0) return;
        var width = Math.Max(60, _setup.ContentWidth / table.Columns.Count);
        foreach (var column in table.Columns) column.Width = new GridLength(width);
    }

    private void OnRemoveColumn(object sender, RoutedEventArgs e)
    {
        if (TableContext() is not { } context) return;
        if (context.Row.Cells.Count <= 1) return;

        var columnIndex = context.Row.Cells.IndexOf(context.Cell);
        foreach (var group in context.Table.RowGroups)
        foreach (var row in group.Rows)
        {
            if (columnIndex < row.Cells.Count) row.Cells.RemoveAt(columnIndex);
        }
        if (columnIndex < context.Table.Columns.Count) context.Table.Columns.RemoveAt(columnIndex);
        RebalanceColumns(context.Table);

        if (context.Row.Cells.Count > 0)
        {
            var cell = context.Row.Cells[Math.Min(columnIndex, context.Row.Cells.Count - 1)];
            Editor.CaretPosition = cell.Blocks.FirstBlock!.ContentStart;
        }
        MarkDirty();
        Editor.Focus();
    }

    private void OnDeleteTable(object sender, RoutedEventArgs e)
    {
        if (TableContext() is not { } context) return;

        Block? next = context.Table.NextBlock;
        switch (context.Table.Parent)
        {
            case FlowDocument document:
                document.Blocks.Remove(context.Table);
                next ??= document.Blocks.LastBlock;
                break;
            case Section section:
                section.Blocks.Remove(context.Table);
                next ??= section.Blocks.LastBlock;
                break;
            default:
                return;
        }

        if (next is not null) Editor.CaretPosition = next.ContentStart;
        TableEditPanel.Visibility = Visibility.Collapsed;
        MarkDirty();
        Editor.Focus();
    }

    private bool MoveTableCell((TableCell Cell, TableRow Row, Table Table) context, bool backwards)
    {
        var rows = context.Table.RowGroups.SelectMany(g => g.Rows).ToList();
        var rowIndex = rows.IndexOf(context.Row);
        var cellIndex = context.Row.Cells.IndexOf(context.Cell);

        TableCell? target = null;
        if (!backwards)
        {
            if (cellIndex + 1 < context.Row.Cells.Count)
            {
                target = context.Row.Cells[cellIndex + 1];
            }
            else if (rowIndex + 1 < rows.Count && rows[rowIndex + 1].Cells.Count > 0)
            {
                target = rows[rowIndex + 1].Cells[0];
            }
            else if (context.Row.Parent is TableRowGroup group)
            {
                // Tab in the last cell grows the table, like Word.
                var newRow = new TableRow();
                foreach (var _ in context.Row.Cells) newRow.Cells.Add(CreateCellLike(context.Cell));
                group.Rows.Insert(group.Rows.IndexOf(context.Row) + 1, newRow);
                target = newRow.Cells[0];
                TableEditPanel.Visibility = Visibility.Visible;
                MarkDirty();
            }
        }
        else if (cellIndex > 0)
        {
            target = context.Row.Cells[cellIndex - 1];
        }
        else if (rowIndex > 0 && rows[rowIndex - 1].Cells.Count > 0)
        {
            var previous = rows[rowIndex - 1];
            target = previous.Cells[previous.Cells.Count - 1];
        }
        else
        {
            return false;
        }

        if (target is null) return false;
        // Let queued keystrokes land in the current cell before jumping to the next one.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (target.Blocks.FirstBlock is { } block) Editor.CaretPosition = block.ContentStart;
            Editor.Focus();
        }));
        return true;
    }

    private void OnInsertImage(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Insertar imagen",
            Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            InsertImageElement(bitmap, bytes);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            MessageBox.Show(this, $"No se pudo insertar la imagen.\n\n{ex.Message}", "Mwrites",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InsertImageElement(BitmapSource bitmap, byte[]? bytes)
    {
        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            MaxWidth = _setup.ContentWidth,
            Tag = bytes
        };

        var paragraph = new Paragraph(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center })
        {
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8)
        };
        DocumentStyles.ApplyStyle(_document, paragraph, DocumentStyles.Normal);

        var caret = CaretParagraph;
        if (caret is not null && string.IsNullOrWhiteSpace(DocumentStats.TextOf(caret)))
        {
            caret.SiblingBlocks?.InsertAfter(caret, paragraph);
        }
        else if (caret?.SiblingBlocks is { } blocks)
        {
            blocks.InsertAfter(caret, paragraph);
        }
        else
        {
            _document.Blocks.Add(paragraph);
        }

        Editor.CaretPosition = paragraph.ContentEnd;
        MarkDirty();
        Editor.Focus();
    }

    private void OnInsertDate(object sender, RoutedEventArgs e)
    {
        var culture = new CultureInfo("es-ES");
        Editor.CaretPosition.InsertTextInRun(DateTime.Today.ToString("d 'de' MMMM 'de' yyyy", culture));
        MarkDirty();
        Editor.Focus();
    }

    private void OnInsertSymbol(object sender, RoutedEventArgs e)
    {
        var dialog = new SymbolDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedSymbol is not null)
        {
            Editor.CaretPosition.InsertTextInRun(dialog.SelectedSymbol);
            MarkDirty();
            Editor.Focus();
        }
    }

    private void OnInsertHorizontalRule(object sender, RoutedEventArgs e)
    {
        var paragraph = new Paragraph
        {
            BorderBrush = DocumentStyles.Frozen("#C9CFD8"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 8, 0, 8)
        };
        DocumentStyles.ApplyStyle(_document, paragraph, DocumentStyles.Normal);

        var caret = CaretParagraph;
        if (caret?.SiblingBlocks is { } blocks)
        {
            blocks.InsertAfter(caret, paragraph);
        }
        else
        {
            _document.Blocks.Add(paragraph);
        }
        Editor.CaretPosition = paragraph.ContentStart;
        MarkDirty();
        Editor.Focus();
    }

    // ================= DISPOSICIÓN =================

    private void OnMarginsMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        AddMarginItem(menu, "Normal (2,54 cm)", 2.54, 2.54);
        AddMarginItem(menu, "Estrecho (1,27 cm)", 1.27, 1.27);
        AddMarginItem(menu, "Moderado (2,54 / 1,91 cm)", 2.54, 1.91);
        AddMarginItem(menu, "Ancho (2,54 / 5,08 cm)", 2.54, 5.08);
        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
    }

    private void AddMarginItem(ContextMenu menu, string name, double verticalCm, double horizontalCm)
    {
        var item = new MenuItem { Header = name };
        item.Click += (_, _) =>
        {
            _setup = _setup.WithMargins(Dip(verticalCm), Dip(horizontalCm), Dip(verticalCm), Dip(horizontalCm));
            ApplyPageSetupChange();
        };
        menu.Items.Add(item);
    }

    private static double Dip(double cm) => cm * 96.0 / 2.54;

    private void OnToggleOrientation(object sender, RoutedEventArgs e)
    {
        _setup = _setup.WithOrientation(!_setup.Landscape);
        ApplyPageSetupChange();
    }

    private void OnSizeMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        AddSizeItem(menu, PageSetup.A4);
        AddSizeItem(menu, PageSetup.Carta);
        AddSizeItem(menu, PageSetup.Oficio);
        AddSizeItem(menu, PageSetup.Ejecutivo);
        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
    }

    private void AddSizeItem(ContextMenu menu, PageSetup preset)
    {
        var item = new MenuItem { Header = $"{preset.SizeName} ({(preset.Width / 96.0 * 2.54):0.#} × {(preset.Height / 96.0 * 2.54):0.#} cm)" };
        item.Click += (_, _) =>
        {
            var updated = _setup
                .WithSize(preset.SizeName, preset.Width, preset.Height)
                .WithOrientation(_setup.Landscape);
            _setup = updated;
            ApplyPageSetupChange();
        };
        menu.Items.Add(item);
    }

    private void OnPageSetup(object sender, RoutedEventArgs e)
    {
        var dialog = new PageSetupDialog(_setup) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        _setup = dialog.Result;
        ApplyPageSetupChange();
        Editor.Focus();
    }

    private void ApplyPageSetupChange()
    {
        _document.Tag = _setup;
        ApplyPageSetupToUi();
        MarkDirty();
        UpdateInfo();
        UpdatePageCount();
    }

    private void ApplyPageSetupToUi()
    {
        PageSheet.Width = _setup.Width;
        PageSheet.MinHeight = _setup.Height;
        Editor.Padding = _setup.Margins;
        Ruler.Width = _setup.Width;
        Ruler.MarginLeft = _setup.MarginLeft;
        Ruler.MarginRight = _setup.MarginRight;
        UpdateRuler();
        _document.PageWidth = _setup.ContentWidth;
        _document.PagePadding = new Thickness(0);
        _document.ColumnWidth = double.NaN;
        UpdatePageBreakOverlaySoon();
    }

    // ================= REVISAR =================

    private void OnToggleSpellCheck(object sender, RoutedEventArgs e)
    {
        SpellCheck.SetIsEnabled(Editor, SpellToggle.IsChecked == true);
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string tag } item) return;
        Editor.Language = XmlLanguage.GetLanguage(tag);
        LanguageStatus.Text = item.Content?.ToString() ?? tag;
    }

    private void InitializeLanguage()
    {
        LanguageCombo.SelectedIndex = 0;
        Editor.Language = XmlLanguage.GetLanguage("es-ES");
        LanguageStatus.Text = "Español (España)";
        SpellCheck.SetIsEnabled(Editor, true);
    }

    private void OnWordCount(object sender, RoutedEventArgs e)
    {
        var selection = Editor.Selection.IsEmpty ? string.Empty : Editor.Selection.Text;
        var dialog = new WordCountDialog(_document, _pageCount, selection) { Owner = this };
        dialog.ShowDialog();
    }

    // ================= VISTA =================

    private void SetZoom(double percent, bool updateSlider)
    {
        _zoomPercent = Math.Clamp(percent, 50, 200);
        var scale = _zoomPercent / 100.0;
        _syncingZoom = true;
        ZoomScale.ScaleX = scale;
        ZoomScale.ScaleY = scale;
        if (updateSlider) ZoomSlider.Value = _zoomPercent;
        ZoomTextButton.Content = $"{Math.Round(_zoomPercent)} %";
        ZoomRibbonText.Text = $"{Math.Round(_zoomPercent)} %";
        _syncingZoom = false;
        UpdateRuler();
    }

    private void OnPageScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateRuler();

    private void UpdateRuler()
    {
        var scale = _zoomPercent / 100.0;
        RulerScale.ScaleX = scale;
        RulerScale.ScaleY = scale;
        var scaledWidth = Ruler.Width * scale;
        var viewport = PageScroll.ViewportWidth > 0 ? PageScroll.ViewportWidth : PageScroll.ActualWidth;
        var left = Math.Max(0, (viewport - scaledWidth) / 2) - PageScroll.HorizontalOffset;
        Canvas.SetLeft(Ruler, left);
        Canvas.SetTop(Ruler, 0);
    }

    private void OnZoomSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _syncingZoom) return;
        SetZoom(e.NewValue, false);
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => SetZoom(_zoomPercent + 10, true);

    private void OnZoomOut(object sender, RoutedEventArgs e) => SetZoom(_zoomPercent - 10, true);

    private void OnZoomReset(object sender, RoutedEventArgs e) => SetZoom(100, true);

    private void OnZoomPageWidth(object sender, RoutedEventArgs e)
    {
        var available = PageScroll.ViewportWidth - 80;
        if (available <= 0) available = 800;
        SetZoom(available / _setup.Width * 100, true);
    }

    private void OnToggleNavPane(object sender, RoutedEventArgs e)
    {
        if (_focusMode) SetFocusMode(false);
        _navVisible = !_navVisible;
        ApplyNavVisibility();
    }

    private void ApplyNavVisibility()
    {
        NavColumn.Width = new GridLength(_navVisible ? _navWidth : 0);
        NavPane.Visibility = _navVisible ? Visibility.Visible : Visibility.Collapsed;
        NavSplitter.Visibility = _navVisible ? Visibility.Visible : Visibility.Collapsed;
        NavPaneToggle.IsChecked = _navVisible;
    }

    private void OnToggleRuler(object sender, RoutedEventArgs e)
    {
        RulerHost.Visibility = RulerToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnToggleGrid(object sender, RoutedEventArgs e)
    {
        GridOverlay.Visibility = GridToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnToggleFocusMode(object sender, RoutedEventArgs e)
    {
        _readingMode = false;
        SetFocusMode(!_focusMode);
    }

    private void OnReadingMode(object sender, RoutedEventArgs e)
    {
        _readingMode = !_readingMode;
        SetFocusMode(_readingMode);
    }

    private void SetFocusMode(bool focus)
    {
        _focusMode = focus;
        if (!focus) _readingMode = false;
        Editor.IsReadOnly = _readingMode;

        RibbonRow.Visibility = focus ? Visibility.Collapsed : Visibility.Visible;
        StatusBar.Visibility = focus ? Visibility.Collapsed : Visibility.Visible;
        NavPane.Visibility = focus || !_navVisible ? Visibility.Collapsed : Visibility.Visible;
        NavColumn.Width = new GridLength(focus || !_navVisible ? 0 : _navWidth);
        NavSplitter.Visibility = focus || !_navVisible ? Visibility.Collapsed : Visibility.Visible;
        FocusToggle.IsChecked = focus;
        FocusStatusToggle.IsChecked = focus;
        PageScroll.Padding = new Thickness(0, focus ? 34 : 14, 0, focus ? 0 : 0);

        if (!focus)
        {
            ApplyNavVisibility();
            Editor.IsReadOnly = false;
            Editor.Focus();
        }
        else
        {
            Editor.Focus();
        }
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (Editor.CanUndo) Editor.Undo();
        Editor.Focus();
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        if (Editor.CanRedo) Editor.Redo();
        Editor.Focus();
    }

    // ================= EVENTOS DEL EDITOR =================

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingDocument || !_ready) return;
        MarkDirty();
        _statsTimer.Stop();
        _statsTimer.Start();
        RefreshHeadingsSoon();
        UpdatePageBreakOverlaySoon();
    }

    private void RefreshHeadingsSoon()
    {
        _headingsTimer.Stop();
        _headingsTimer.Start();
    }

    private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _loadingDocument) return;
        UpdateToolbarState();
    }

    private void UpdateToolbarState()
    {
        _syncingToolbar = true;
        try
        {
            var selection = Editor.Selection;

            if (selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily family)
            {
                SelectFontFamilyItem(family.Source);
            }

            if (selection.GetPropertyValue(TextElement.FontSizeProperty) is double sizeDip)
            {
                SelectFontSizeItem(Math.Round(sizeDip * 72.0 / 96.0));
            }

            var weight = selection.GetPropertyValue(TextElement.FontWeightProperty);
            BoldToggle.IsChecked = weight is FontWeight fontWeight &&
                                   fontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight();

            var style = selection.GetPropertyValue(TextElement.FontStyleProperty);
            ItalicToggle.IsChecked = style is FontStyle fontStyle && fontStyle == FontStyles.Italic;

            var decorations = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            UnderlineToggle.IsChecked = decorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true;
            StrikeToggle.IsChecked = decorations?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true;

            var baseline = selection.GetPropertyValue(Inline.BaselineAlignmentProperty);
            SubscriptToggle.IsChecked = baseline is BaselineAlignment.Subscript;
            SuperscriptToggle.IsChecked = baseline is BaselineAlignment.Superscript;

            var key = CaretParagraph is { } paragraph ? DocumentStyles.StyleKeyOf(paragraph) : DocumentStyles.Normal;
            _suppressGallery = true;
            StyleGallery.SelectedItem = StyleGallery.Items
                .OfType<ListBoxItem>()
                .FirstOrDefault(item => (item.Tag as string) == key);
            _suppressGallery = false;

            TableEditPanel.Visibility = TableContext() is null ? Visibility.Collapsed : Visibility.Visible;
        }
        finally
        {
            _syncingToolbar = false;
        }
    }

    private void SelectFontFamilyItem(string name)
    {
        foreach (var item in FontFamilyCombo.Items)
        {
            if (item is string text && string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
            {
                FontFamilyCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void SelectFontSizeItem(double points)
    {
        double? best = null;
        var distance = double.MaxValue;
        foreach (var item in FontSizeCombo.Items)
        {
            if (item is double value && Math.Abs(value - points) < distance)
            {
                distance = Math.Abs(value - points);
                best = value;
            }
        }
        if (best is not null && distance <= 2) FontSizeCombo.SelectedItem = best;
    }

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            QueueNormalAfterStyledEnter();
        }

        if (e.Key == Key.Tab && TableContext() is { } tableContext)
        {
            if (MoveTableCell(tableContext, backwards: Keyboard.Modifiers == ModifierKeys.Shift))
            {
                e.Handled = true;
                return;
            }
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
        {
            OnPageBreak(sender, e);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.B:
                case Key.I:
                case Key.U:
                    // Let the RichTextBox editing commands handle these: they correctly
                    // apply the pending format to the caret. Just refresh the ribbon after.
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateToolbarState));
                    break;
                case Key.E:
                    OnAlignCenter(sender, e);
                    e.Handled = true;
                    break;
                case Key.L:
                    OnAlignLeft(sender, e);
                    e.Handled = true;
                    break;
                case Key.R:
                    OnAlignRight(sender, e);
                    e.Handled = true;
                    break;
                case Key.J:
                    OnAlignJustify(sender, e);
                    e.Handled = true;
                    break;
            }
        }
    }

    // Word applies the "next paragraph style" when Enter is pressed at the end of a heading;
    // without this the new paragraph would keep the heading style and everything typed next
    // would stay blue and bold.
    private void QueueNormalAfterStyledEnter()
    {
        if (!Editor.Selection.IsEmpty) return;

        var paragraph = Editor.CaretPosition.Paragraph;
        if (paragraph is null) return;
        if (DocumentStyles.StyleKeyOf(paragraph) == DocumentStyles.Normal) return;
        if (paragraph.Parent is not (FlowDocument or Section)) return;

        var caret = Editor.CaretPosition;
        if (caret.CompareTo(paragraph.ContentEnd) != 0 &&
            new TextRange(caret, paragraph.ContentEnd).Text.Trim().Length > 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            var current = Editor.CaretPosition.Paragraph;
            if (current is null || ReferenceEquals(current, paragraph)) return;
            if (current.Parent is not (FlowDocument or Section)) return;
            DocumentStyles.ApplyStyle(_document, current, DocumentStyles.Normal);
            UpdateToolbarState();
        }));
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e) => HandleFileDrop(e);

    private void OnEditorDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        HandleFileDrop(e);
    }

    private void HandleFileDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        var file = files[0];
        var extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
        if (extension is not (".docx" or ".rtf" or ".txt")) return;
        if (!ConfirmSaveIfDirty()) return;
        OpenPath(file);
    }

    // ================= NAVEGACIÓN =================

    private void RefreshHeadings()
    {
        var paragraphs = new List<Paragraph>();
        DocumentStats.Collect(_document.Blocks, paragraphs);

        var headings = paragraphs
            .Select(p => new
            {
                Paragraph = p,
                Key = DocumentStyles.StyleKeyOf(p),
                Text = DocumentStats.TextOf(p).Trim()
            })
            .Where(item => DocumentStyles.IsHeading(item.Key) && item.Text.Length > 0)
            .Take(200)
            .ToList();

        _suppressHeadings = true;
        HeadingList.Items.Clear();
        foreach (var heading in headings)
        {
            var level = DocumentStyles.HeadingLevel(heading.Key);
            var text = heading.Text.Length > 80 ? heading.Text[..80] + "…" : heading.Text;
            var label = new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                FontWeight = level <= 1 ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(Math.Max(0, (level - 1) * 12), 0, 0, 0),
                Foreground = level switch
                {
                    0 => DocumentStyles.Frozen("#1F3864"),
                    1 => DocumentStyles.Frozen("#2E74B5"),
                    _ => DocumentStyles.Frozen("#404040")
                }
            };
            var item = new ListBoxItem { Content = label, Tag = heading.Paragraph, Style = (Style)FindResource("NavItem") };
            HeadingList.Items.Add(item);
        }
        _suppressHeadings = false;

        NoHeadingsText.Visibility = headings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HeadingList.Visibility = headings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRefreshHeadings(object sender, RoutedEventArgs e)
    {
        RefreshHeadings();
    }

    private void OnHeadingSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHeadings) return;
        if (HeadingList.SelectedItem is not ListBoxItem { Tag: Paragraph paragraph }) return;
        Editor.CaretPosition = paragraph.ContentStart;
        Editor.Selection.Select(paragraph.ContentStart, paragraph.ContentStart);
        Editor.Focus();

        _suppressHeadings = true;
        HeadingList.SelectedItem = null;
        _suppressHeadings = false;
    }

    // ================= RECUENTO Y PÁGINAS =================

    private void UpdateStats()
    {
        var stats = DocumentStats.Compute(_document);
        WordCountText.Text = stats.Words == 1 ? "1 palabra" : $"{stats.Words:N0} palabras";
        PageCountText.Text = _pageCount == 1 ? "1 página" : $"{_pageCount:N0} páginas";
        InfoWords.Text = stats.Words.ToString("N0");
    }

    private void UpdatePageCount()
    {
        try
        {
            _pageCount = MeasurePages();
        }
        catch (Exception)
        {
            _pageCount = 1;
        }
        PageCountText.Text = _pageCount == 1 ? "1 página" : $"{_pageCount:N0} páginas";
        InfoPages.Text = _pageCount.ToString();
    }

    private int MeasurePages()
    {
        var oldWidth = _document.PageWidth;
        var oldHeight = _document.PageHeight;
        var oldPadding = _document.PagePadding;
        var oldColumn = _document.ColumnWidth;
        try
        {
            _document.PageWidth = _setup.Width;
            _document.PageHeight = _setup.Height;
            _document.PagePadding = _setup.Margins;
            _document.ColumnWidth = _setup.ContentWidth;

            var paginator = ((IDocumentPaginatorSource)_document).DocumentPaginator;
            paginator.PageSize = new Size(_setup.Width, _setup.Height);
            paginator.ComputePageCount();
            return Math.Max(1, paginator.PageCount);
        }
        finally
        {
            _document.PageWidth = oldWidth;
            _document.PageHeight = oldHeight;
            _document.PagePadding = oldPadding;
            _document.ColumnWidth = oldColumn;
        }
    }

    // ================= BÚSQUEDA =================

    private void OnFindCommand(object sender, RoutedEventArgs e) => ShowFindReplace(false);

    private void OnReplaceCommand(object sender, RoutedEventArgs e) => ShowFindReplace(true);

    private void ShowFindReplace(bool replace)
    {
        var dialog = new FindReplaceDialog(Editor, replace) { Owner = this };
        if (!Editor.Selection.IsEmpty)
        {
            var selected = Editor.Selection.Text;
            if (selected.Length <= 120) dialog.SearchBox.Text = selected;
        }
        dialog.ShowDialog();
        Editor.Focus();
    }

    // ================= IMPRESIÓN =================

    private void PrintDocument()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        var oldWidth = _document.PageWidth;
        var oldHeight = _document.PageHeight;
        var oldPadding = _document.PagePadding;
        var oldColumn = _document.ColumnWidth;
        try
        {
            _document.PageWidth = _setup.Width;
            _document.PageHeight = _setup.Height;
            _document.PagePadding = _setup.Margins;
            _document.ColumnWidth = _setup.ContentWidth;

            var paginator = ((IDocumentPaginatorSource)_document).DocumentPaginator;
            paginator.PageSize = new Size(_setup.Width, _setup.Height);
            dialog.PrintDocument(paginator, DocumentName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo imprimir el documento.\n\n{ex.Message}", "Mwrites",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _document.PageWidth = oldWidth;
            _document.PageHeight = oldHeight;
            _document.PagePadding = oldPadding;
            _document.ColumnWidth = oldColumn;
        }
    }

    // ================= ÁREA DE ARCHIVO =================

    private void OnFileButtonClick(object sender, RoutedEventArgs e)
    {
        ShowBackstage();
    }

    private void ShowBackstage()
    {
        Backstage.Visibility = Visibility.Visible;
        UpdateInfo();
        UpdateRecentList();
        _suppressBackstage = true;
        BackstageList.SelectedIndex = 0;
        _suppressBackstage = false;
        ShowInfoPane();
    }

    private void HideBackstage()
    {
        Backstage.Visibility = Visibility.Collapsed;
        _suppressBackstage = true;
        BackstageList.SelectedItem = null;
        _suppressBackstage = false;
        Editor.Focus();
    }

    private void OnCloseBackstage(object sender, RoutedEventArgs e) => HideBackstage();

    private void OnBackstageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressBackstage) return;
        if (BackstageList.SelectedItem is not ListBoxItem item) return;
        switch (item.Tag as string)
        {
            case "Info":
                ShowInfoPane();
                break;
            case "New":
                ShowTemplatePane();
                break;
            case "Open":
                HideBackstage();
                OpenFileDialogInteractive();
                break;
            case "Save":
                HideBackstage();
                SaveDocument();
                break;
            case "SaveAs":
                HideBackstage();
                SaveDocumentAs();
                break;
            case "Print":
                HideBackstage();
                PrintDocument();
                break;
            case "Close":
                Close();
                break;
        }
    }

    private void ShowInfoPane()
    {
        BackstageInfoPane.Visibility = Visibility.Visible;
        BackstageTemplatePane.Visibility = Visibility.Collapsed;
    }

    private void ShowTemplatePane()
    {
        BackstageInfoPane.Visibility = Visibility.Collapsed;
        BackstageTemplatePane.Visibility = Visibility.Visible;
    }

    private void UpdateRecentList()
    {
        _suppressRecent = true;
        RecentList.Items.Clear();
        var recent = RecentFiles.Load();
        foreach (var document in recent)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = document.Title,
                FontSize = 12.5,
                Foreground = DocumentStyles.Frozen("#252423"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            panel.Children.Add(new TextBlock
            {
                Text = document.Path,
                FontSize = 10.5,
                Foreground = DocumentStyles.Frozen("#8A8886"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0)
            });
            RecentList.Items.Add(new ListBoxItem
            {
                Content = panel,
                Tag = document.Path,
                ToolTip = document.Path,
                Style = (Style)FindResource("NavItem")
            });
        }
        NoRecentText.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _suppressRecent = false;
    }

    private void OnRecentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressRecent) return;
        if (RecentList.SelectedItem is not ListBoxItem { Tag: string path }) return;

        _suppressRecent = true;
        RecentList.SelectedItem = null;
        _suppressRecent = false;

        if (!File.Exists(path))
        {
            MessageBox.Show(this, "El documento ya no está disponible en esa ubicación.", "Mwrites",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!ConfirmSaveIfDirty()) return;
        OpenPath(path);
    }

    private void BuildTemplateCards()
    {
        foreach (var template in Templates.All)
        {
            TemplateCards.Items.Add(CreateTemplateCard(template));
            TemplateCardsLarge.Items.Add(CreateTemplateCard(template));
        }
    }

    private Button CreateTemplateCard(Templates.Template template)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = template.Name,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = DocumentStyles.Frozen("#252423"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock
        {
            Text = template.Description,
            FontSize = 11,
            Foreground = DocumentStyles.Frozen("#605E5C"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });

        var card = new Button
        {
            Style = (Style)FindResource("CardButton"),
            Content = panel,
            ToolTip = $"Crear un documento con la plantilla {template.Name}"
        };
        card.Click += (_, _) => NewFromTemplate(template);
        return card;
    }

    private void NewFromTemplate(Templates.Template template)
    {
        if (!ConfirmSaveIfDirty()) return;
        var document = template.Create(PageSetup.A4);
        LoadDocument(document, null);
        HideBackstage();
        Editor.Focus();
    }

    // ================= UTILIDADES =================

    private void FillCombos()
    {
        _suppressList = true;
        FontFamilyCombo.Items.Clear();
        foreach (var family in new[]
                 {
                     "Calibri", "Calibri Light", "Cambria", "Georgia", "Times New Roman",
                     "Arial", "Verdana", "Tahoma", "Segoe UI", "Consolas", "Courier New"
                 })
        {
            FontFamilyCombo.Items.Add(family);
        }
        FontFamilyCombo.SelectedItem = "Calibri";

        FontSizeCombo.Items.Clear();
        foreach (var size in new double[] { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48, 72 })
        {
            FontSizeCombo.Items.Add(size);
        }
        FontSizeCombo.SelectedItem = 11d;

        LineSpacingCombo.Items.Clear();
        foreach (var (name, factor) in new (string, double)[]
                 {
                     ("1,0", 1.0), ("1,15", 1.15), ("1,5", 1.5), ("2,0", 2.0)
                 })
        {
            LineSpacingCombo.Items.Add(new ComboBoxItem { Content = name, Tag = factor });
        }
        LineSpacingCombo.SelectedIndex = 0;
        _suppressList = false;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (Backstage.Visibility == Visibility.Visible)
            {
                HideBackstage();
                e.Handled = true;
            }
            else if (_focusMode)
            {
                _readingMode = false;
                SetFocusMode(false);
                e.Handled = true;
            }
        }
    }
}
