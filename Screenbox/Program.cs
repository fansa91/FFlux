using System;
using System.IO;
using System.Threading;
using Screenbox;

public static class Program
{
    // Unpackaged WinUI 3 entry point. The UWP AppInstance / IActivatedEventArgs model
    // requires a package identity (0x80073D57 here), so multi-instance redirection and
    // file activation have to be rebuilt on Microsoft.Windows.AppLifecycle plus the
    // command line args below. Until that lands this starts a single window directly.
    [STAThread]
    static void Main(string[] args)
    {
        // args will carry files opened from Explorer / the command line.
        _ = args;

        WinRT.ComWrappersSupport.InitializeComWrappers();

        Microsoft.UI.Xaml.Application.Start(p =>
        {
            _ = p;
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
                _ = new App();
            }
            catch (Exception ex)
            {
                // Application.Start swallows callback failures on some WinRT hosts, so
                // persist the real startup exception before rethrowing.
                try
                {
                    string logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Screenbox", "startup-error.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                    File.WriteAllText(logPath, ex.ToString());
                }
                catch
                {
                    // Logging must never mask the original failure.
                }

                throw;
            }
        });
    }
}
