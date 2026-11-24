using Avalonia;
using MwritesApp = Mwrites.Portable.App.App;

namespace Mwrites.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<MwritesApp>().UsePlatformDetect();
}
