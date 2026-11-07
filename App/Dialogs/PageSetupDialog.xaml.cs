using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mwrites.Core.Documents;

namespace Mwrites.Dialogs;

public partial class PageSetupDialog : Window
{
    private const double DipPerCm = 96.0 / 2.54;

    public PageSetup? Result { get; private set; }

    public PageSetupDialog(PageSetup current)
    {
        InitializeComponent();

        SizeCombo.Items.Add(new ComboBoxItem { Content = "A4 (21 × 29,7 cm)", Tag = PageSetup.A4 });
        SizeCombo.Items.Add(new ComboBoxItem { Content = "Carta (21,6 × 27,9 cm)", Tag = PageSetup.Carta });
        SizeCombo.Items.Add(new ComboBoxItem { Content = "Oficio (21,6 × 33 cm)", Tag = PageSetup.Oficio });
        SizeCombo.Items.Add(new ComboBoxItem { Content = "Ejecutivo (18,4 × 26,7 cm)", Tag = PageSetup.Ejecutivo });

        var index = 0;
        for (var i = 0; i < SizeCombo.Items.Count; i++)
        {
            if ((SizeCombo.Items[i] as ComboBoxItem)?.Tag is PageSetup preset &&
                preset.SizeName == current.SizeName.Replace(" (horizontal)", ""))
            {
                index = i;
            }
        }
        SizeCombo.SelectedIndex = index;
        LandscapeRadio.IsChecked = current.Landscape;
        PortraitRadio.IsChecked = !current.Landscape;

        TopBox.Text = ToCm(current.MarginTop);
        BottomBox.Text = ToCm(current.MarginBottom);
        LeftBox.Text = ToCm(current.MarginLeft);
        RightBox.Text = ToCm(current.MarginRight);

        UpdatePreview();
    }

    private static string ToCm(double dip) => (dip / DipPerCm).ToString("0.00", CultureInfo.CurrentCulture);

    private void OnSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (PreviewPage is null) return;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (SizeCombo.SelectedItem is not ComboBoxItem { Tag: PageSetup preset }) return;
        var landscape = LandscapeRadio.IsChecked == true;
        var width = landscape ? preset.Height : preset.Width;
        var height = landscape ? preset.Width : preset.Height;
        var scale = Math.Min(92.0 / width, 100.0 / height);
        PreviewPage.Width = width * scale;
        PreviewPage.Height = height * scale;

        var left = ParseMargin(LeftBox.Text);
        var right = ParseMargin(RightBox.Text);
        var top = ParseMargin(TopBox.Text);
        var bottom = ParseMargin(BottomBox.Text);
        var contentWidth = Math.Max(2, (width - left - right) * scale);
        var contentHeight = Math.Max(2, (height - top - bottom) * scale);
        PreviewContent.Margin = new Thickness(left * scale, top * scale, right * scale, bottom * scale);
        PreviewContent.Width = contentWidth;
        PreviewContent.Height = contentHeight;
    }

    private static double ParseMargin(string text)
    {
        if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cm))
        {
            return Math.Clamp(cm, 0, 10) * DipPerCm;
        }
        return 2.54 * DipPerCm;
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (SizeCombo.SelectedItem is not ComboBoxItem { Tag: PageSetup preset })
        {
            MessageBox.Show(this, "Selecciona un tamaño de papel.", "Mwrites", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        double top, right, bottom, left;
        try
        {
            top = ParseCm(TopBox.Text);
            right = ParseCm(RightBox.Text);
            bottom = ParseCm(BottomBox.Text);
            left = ParseCm(LeftBox.Text);
        }
        catch (FormatException)
        {
            MessageBox.Show(this, "Los márgenes deben ser números en centímetros (por ejemplo 2,54).",
                "Mwrites", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var setup = preset
            .WithOrientation(LandscapeRadio.IsChecked == true)
            .WithMargins(top, right, bottom, left);
        Result = setup;
        DialogResult = true;
    }

    private static double ParseCm(string text)
    {
        if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cm))
            throw new FormatException();
        if (cm < 0.5 || cm > 10)
            throw new FormatException();
        return cm * DipPerCm;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
