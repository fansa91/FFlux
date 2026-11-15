using System;
using System.Threading.Tasks;

namespace Screenbox.Core.Helpers;

/// <summary>
/// The one allowed shape for fire-and-forget work in Core: every task is observed, so an
/// exception can never escape from a messenger callback or a native event handler.
/// </summary>
/// <remarks>
/// An <c>async void</c> handler that throws on a thread with no WinUI
/// <c>SynchronizationContext</c> does not reach <c>Application.UnhandledException</c>.
/// CoreMessaging turns it into a stowed exception (0xC000027B) and kills the process without a
/// managed stack - the "random crash with nothing in the log" failure mode. Replacing those
/// handlers with <c>void</c> methods that call <see cref="Run"/> keeps the exception observable
/// and the process alive.
/// </remarks>
public static class FireAndForget
{
    private static Action<string, Exception>? _errorHandler;

    /// <summary>
    /// Installs the process-wide sink. The app wires this to its on-disk startup log; a plain
    /// test host can leave it unset, in which case failures are simply dropped (never rethrown).
    /// </summary>
    public static void SetErrorHandler(Action<string, Exception> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _errorHandler = handler;
    }

    /// <summary>Removes the sink (test isolation).</summary>
    public static void ResetErrorHandler() => _errorHandler = null;

    /// <summary>
    /// Reports an exception already caught by a legacy <c>async void</c> handler, so all such
    /// failures land in the same place. Never throws.
    /// </summary>
    public static void Report(string context, Exception exception)
    {
        try
        {
            _errorHandler?.Invoke(context, exception);
        }
        catch (Exception)
        {
            // Reporting must never become the exception that escapes the callback.
        }
    }

    /// <summary>
    /// Starts <paramref name="operation"/> and observes its outcome. The synchronous part runs on
    /// the caller's thread, so an operation started on the UI thread still resumes there after
    /// its own awaits.
    /// </summary>
    public static void Run(Func<Task> operation, string context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _ = ObserveAsync(operation, context);
    }

    private static async Task ObserveAsync(Func<Task> operation, string context)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                _errorHandler?.Invoke(context, ex);
            }
            catch (Exception)
            {
                // Reporting must never become the exception that escapes the callback.
            }
        }
    }
}
