using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Screenbox;
using Screenbox.Helpers;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using DispatcherQueueHandler = Microsoft.UI.Dispatching.DispatcherQueueHandler;

public static class Program
{
    private const int MaxFirstChanceEntries = 400;
    private static int _firstChanceCount;

    // The logger itself may touch APIs that throw; never recurse into the handler.
    [ThreadStatic] private static bool _inFirstChanceHandler;

    // Unpackaged WinUI 3 entry point. The UWP AppInstance / IActivatedEventArgs model
    // requires a package identity (0x80073D57 here), so multi-instance redirection and
    // file activation have to be rebuilt on Microsoft.Windows.AppLifecycle plus the
    // command line args below. Until that lands this starts a single window directly.
    [STAThread]
    static void Main(string[] args)
    {
        // Unpackaged WinUI 3 has no file-type activation, so the command line is the
        // primary way Explorer / a terminal hands media files to the app.
        LaunchArguments.Capture(args);
        InstallDiagnostics();
        StartupErrorLog.Write($"Main entered ({LaunchArguments.Files.Count} media file(s) on the command line)");
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            StartupErrorLog.Write("ComWrappers initialized");

            Microsoft.UI.Xaml.Application.Start(p =>
            {
                _ = p;
                StartupErrorLog.Write("Application.Start callback");
                try
                {
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherQueueSynchronizationContext(
                            DispatcherQueue.GetForCurrentThread()));
                    _ = new App();
                    StartupErrorLog.Write("App constructed");
                }
                catch (Exception ex)
                {
                    StartupErrorLog.Write("App construction failed: " + ex);
                    throw;
                }
            });

            StartupErrorLog.Write("Application.Start returned");
        }
        catch (Exception ex)
        {
            StartupErrorLog.Write("Startup failed: " + ex);
            throw;
        }
    }

    /// <summary>
    /// A stowed exception (0xC000027B) raised on the CoreMessaging thread never reaches
    /// Application.UnhandledException. First-chance capture is AppDomain-wide and still
    /// sees the projected COMException before it crosses the ABI boundary, so it is the
    /// only reliable way to get a managed stack for this failure. Set
    /// FFLUX_STARTUP_TRACE=1 to enable it for a diagnostic run; it is otherwise
    /// off because XAML startup produces a large number of benign first-chance throws.
    /// </summary>
    private static void InstallDiagnostics()
    {
        if (Environment.GetEnvironmentVariable("FFLUX_STARTUP_TRACE") == "1")
        {
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => StartupErrorLog.Write("ProcessExit");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            StartupErrorLog.Write("UnobservedTaskException: " + e.Exception);
            e.SetObserved();
        };
    }

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        if (_inFirstChanceHandler) return;
        _inFirstChanceHandler = true;
        try
        {
            Exception ex = e.Exception;
            const int invalidArg = unchecked((int)0x80070057);
            bool interesting = ex is COMException
                               || ex is ArgumentException
                               || ex is InvalidOperationException
                               || ex is NotSupportedException
                               || ex is SEHException
                               || ex.HResult == invalidArg;
            if (!interesting) return;
            if (Interlocked.Increment(ref _firstChanceCount) > MaxFirstChanceEntries) return;

            StartupErrorLog.Write(
                $"[FC] {ex.GetType().FullName} hr=0x{ex.HResult:X8} {ex.Message}{Environment.NewLine}{ex.StackTrace}");
        }
        finally
        {
            _inFirstChanceHandler = false;
        }
    }
}
