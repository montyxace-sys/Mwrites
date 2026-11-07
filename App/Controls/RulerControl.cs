using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Mwrites.Controls;

public sealed class RulerControl : FrameworkElement
{
    private const double DipPerCm = 96.0 / 2.54;

    public static readonly DependencyProperty MarginLeftProperty = DependencyProperty.Register(
        nameof(MarginLeft), typeof(double), typeof(RulerControl),
        new FrameworkPropertyMetadata(90.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarginRightProperty = DependencyProperty.Register(
        nameof(MarginRight), typeof(double), typeof(RulerControl),
        new FrameworkPropertyMetadata(90.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double MarginLeft
    {
        get => (double)GetValue(MarginLeftProperty);
        set => SetValue(MarginLeftProperty, value);
    }

    public double MarginRight
    {
        get => (double)GetValue(MarginRightProperty);
        set => SetValue(MarginRightProperty, value);
    }

    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xF9, 0xF8)));
    private static readonly Brush MarginBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xEF, 0xED, 0xEB)));
    private static readonly Brush TickBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x88, 0x86)));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0x5E, 0x5C)));
    private static readonly Pen TickPen = CreatePen(TickBrush, 1);
    private static readonly Pen BorderPen = CreatePen(Freeze(new SolidColorBrush(Color.FromRgb(0xD2, 0xD0, 0xCE))), 1);
    private static readonly Typeface Typeface = new("Segoe UI");

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        var bounds = new Rect(0, 0, width, height);
        dc.DrawRectangle(BackgroundBrush, null, bounds);

        var left = Math.Clamp(MarginLeft, 0, width);
        var right = Math.Clamp(width - MarginRight, left, width);
        if (left > 0) dc.DrawRectangle(MarginBrush, null, new Rect(0, 0, left, height));
        if (right < width) dc.DrawRectangle(MarginBrush, null, new Rect(right, 0, width - right, height));

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var cm = 0; left + cm * DipPerCm <= right + 0.01; cm++)
        {
            var x = left + cm * DipPerCm;
            dc.DrawLine(TickPen, new Point(x, height - 7), new Point(x, height - 1));

            var text = new FormattedText(
                cm.ToString(CultureInfo.InvariantCulture),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                Typeface,
                8.5,
                TextBrush,
                pixelsPerDip);
            dc.DrawText(text, new Point(x - text.Width / 2, 1));

            for (var quarter = 1; quarter < 4; quarter++)
            {
                var minorX = x + quarter / 4.0 * DipPerCm;
                if (minorX >= right) break;
                var minorHeight = quarter == 2 ? 5.0 : 3.0;
                dc.DrawLine(TickPen, new Point(minorX, height - minorHeight), new Point(minorX, height - 1));
            }
        }

        dc.DrawLine(TickPen, new Point(left, 0), new Point(left, height));
        dc.DrawLine(TickPen, new Point(right, 0), new Point(right, height));
        dc.DrawLine(BorderPen, new Point(0, height - 0.5), new Point(width, height - 0.5));
        dc.DrawRectangle(null, BorderPen, new Rect(0.5, 0.5, width - 1, height - 1));
    }
}
