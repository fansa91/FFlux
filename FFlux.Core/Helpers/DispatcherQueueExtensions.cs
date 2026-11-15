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
