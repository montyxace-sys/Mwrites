using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Mwrites;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "mwrites-error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:O}\n{e.Exception}\n\n");
        }
        catch (IOException)
        {
        }

        MessageBox.Show(
            $"Ha ocurrido un error inesperado.\n\n{e.Exception.Message}",
            "Mwrites",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
