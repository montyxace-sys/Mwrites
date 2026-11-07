using System.Windows;

namespace Mwrites.Core.Documents;

public sealed record PageSetup(
    string SizeName,
    double Width,
    double Height,
    double MarginTop,
    double MarginRight,
    double MarginBottom,
    double MarginLeft)
{
    public const double MillimetersToDip = 96.0 / 25.4;
    public const double TwipsToDip = 96.0 / 1440.0;
    public const double DipToTwips = 1440.0 / 96.0;

    public static double Mm(double millimeters) => millimeters * MillimetersToDip;

    public static readonly PageSetup A4 = new("A4", Mm(210), Mm(297), Mm(25.4), Mm(25.4), Mm(25.4), Mm(25.4));
    public static readonly PageSetup Carta = new("Carta", Mm(215.9), Mm(279.4), Mm(25.4), Mm(25.4), Mm(25.4), Mm(25.4));
    public static readonly PageSetup Oficio = new("Oficio", Mm(215.9), Mm(330.2), Mm(25.4), Mm(25.4), Mm(25.4), Mm(25.4));
    public static readonly PageSetup Ejecutivo = new("Ejecutivo", Mm(184.15), Mm(266.7), Mm(19.05), Mm(19.05), Mm(19.05), Mm(19.05));

    public double ContentWidth => Width - MarginLeft - MarginRight;

    public double ContentHeight => Height - MarginTop - MarginBottom;

    public Thickness Margins => new(MarginLeft, MarginTop, MarginRight, MarginBottom);

    public bool Landscape => Width > Height;

    public PageSetup WithOrientation(bool landscape)
    {
        if (landscape == Landscape) return this;
        var (top, right, bottom, left) = landscape
            ? (MarginLeft, MarginTop, MarginRight, MarginBottom)
            : (MarginRight, MarginBottom, MarginLeft, MarginTop);
        return this with { Width = Height, Height = Width, MarginTop = top, MarginRight = right, MarginBottom = bottom, MarginLeft = left };
    }

    public PageSetup WithMargins(double top, double right, double bottom, double left) =>
        this with { MarginTop = top, MarginRight = right, MarginBottom = bottom, MarginLeft = left };

    public PageSetup WithSize(string name, double width, double height) =>
        this with { SizeName = name, Width = width, Height = height };

    public static PageSetup FromTwips(double widthTwips, double heightTwips, double topTwips, double rightTwips, double bottomTwips, double leftTwips)
    {
        var width = widthTwips * TwipsToDip;
        var height = heightTwips * TwipsToDip;
        var setup = new PageSetup("Personalizado", width, height,
            topTwips * TwipsToDip, rightTwips * TwipsToDip, bottomTwips * TwipsToDip, leftTwips * TwipsToDip);
        return setup.DetectName();
    }

    public PageSetup DetectName()
    {
        var tolerance = Mm(2);
        foreach (var preset in new[] { A4, Carta, Oficio, Ejecutivo })
        {
            var portrait = Math.Abs(Width - preset.Width) <= tolerance && Math.Abs(Height - preset.Height) <= tolerance;
            var landscape = Math.Abs(Width - preset.Height) <= tolerance && Math.Abs(Height - preset.Width) <= tolerance;
            if (portrait || landscape)
            {
                var name = landscape ? preset.SizeName + " (horizontal)" : preset.SizeName;
                return this with { SizeName = name };
            }
        }
        return this with { SizeName = "Personalizado" };
    }
}
