using System;
using System.Runtime.CompilerServices;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Dispatcher helpers written against <see cref="IAppDispatcher"/> so they work
/// unchanged in the WinUI app and in a plain desktop test host.
/// </summary>
public static class DispatcherQueueExtensions
{
    private static readonly ConditionalWeakTable<IAppDispatcherTimer, DebounceState> DebounceStates = new();

    /// <summary>
    /// Runs <paramref name="action"/> inline when already on the UI thread,
    /// otherwise posts it to the dispatcher.
    /// </summary>
    public static bool TryExecuteOrEnqueue(this IAppDispatcher dispatcher, Action action)
    {
        if (dispatcher.HasThreadAccess)
        {
            action();
            return true;
        }

        return dispatcher.TryEnqueue(action);
    }

    /// <summary>
    /// Runs a WinRT / COM event callback on the UI thread and guarantees that no exception
    /// escapes back into the native caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SMTC button presses, seek requests and MediaPlayer events are raised on a background
    /// MTA worker thread. An exception thrown there never reaches
    /// <c>Application.UnhandledException</c>: CoreMessaging wraps it as a stowed exception
    /// (0xC000027B) and terminates the process without a managed stack. That is what makes
    /// these failures look like a random crash with no log entry.
    /// </para>
    /// <para>
    /// The guard covers both hazards at once: the body runs on the dispatcher (so observable
    /// collections and bound view models are never touched cross-thread), and every throw —
    /// including one from <c>HasThreadAccess</c> / <c>TryEnqueue</c> during shutdown — is
    /// swallowed and passed to <paramref name="onError"/> for diagnostics.
    /// </para>
    /// </remarks>
    public static bool TryExecuteGuarded(
        this IAppDispatcher dispatcher,
        Action action,
        Action<Exception>? onError = null)
    {
        void Report(Exception ex)
        {
            try
            {
                onError?.Invoke(ex);
            }
            catch (Exception)
            {
                // Reporting must never become the exception that escapes the callback.
            }
        }

        void Guarded()
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        try
        {
            return dispatcher.TryExecuteOrEnqueue(Guarded);
        }
        catch (Exception ex)
        {
            // HasThreadAccess / TryEnqueue can fail while the dispatcher is being torn down.
            Report(ex);
            return false;
        }
    }

    /// <summary>
    /// WinUI 3 compatibility shim for the CommunityToolkit <c>Debounce</c> helper.
    /// Each call restarts the timer; the action runs once the interval elapses.
    /// With <paramref name="immediate"/> set, the first call in a quiet window runs
    /// right away and later calls within the window only push the deadline out.
    /// </summary>
    public static void DebounceCore(
        this IAppDispatcherTimer timer,
        Action action,
        TimeSpan interval,
        bool immediate = false)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(action);

        DebounceState state = DebounceStates.GetValue(timer, static _ => new DebounceState());
        state.Action = action;
        state.Immediate = immediate;

        if (state.Handler is null)
        {
            state.Handler = (_, _) =>
            {
                timer.Stop();
                state.ImmediateExecuted = false;
                if (!state.Immediate)
                {
                    state.Action?.Invoke();
                }
            };

            timer.Tick += state.Handler;
        }

        timer.Interval = interval;
        timer.Stop();

        if (immediate && !state.ImmediateExecuted)
        {
            state.ImmediateExecuted = true;
            action();
        }

        timer.Start();
    }

    private sealed class DebounceState
    {
        public Action? Action;
        public bool Immediate;
        public bool ImmediateExecuted;
        public EventHandler? Handler;
    }
}
