using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SkyrimModSplitter.Gui;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += LogUnhandledException;
    }

    private static void LogUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Environment.CurrentDirectory, "artifacts", "logs");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "gui-crash.log"), DateTime.UtcNow.ToString("O") + Environment.NewLine + e.Exception);
        }
        catch { }
        MessageBox.Show(e.Exception.Message, "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
