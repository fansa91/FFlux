using System;
using System.Runtime.CompilerServices;
using Windows.Foundation;
using Windows.System;

namespace Screenbox.Core.Helpers;

public static class DispatcherQueueExtensions
{
    private static readonly ConditionalWeakTable<DispatcherQueueTimer, DebounceState> DebounceStates = new();

    public static bool TryExecuteOrEnqueue(this DispatcherQueue dispatcherQueue, DispatcherQueueHandler action)
    {
        if (dispatcherQueue.HasThreadAccess)
        {
            action();
            return true;
        }
        else
        {
            return dispatcherQueue.TryEnqueue(action);
        }
    }

    /// <summary>
    /// WinUI 3 compatibility shim for the CommunityToolkit <c>Debounce</c> helper.
    /// CommunityToolkit.WinUI targets <c>Microsoft.UI.Dispatching.DispatcherQueueTimer</c>,
    /// while Screenbox still uses the system <c>Windows.System.DispatcherQueue</c>, which is
    /// valid in unpackaged WinUI 3 apps. Each call restarts the timer; the action runs once
    /// the interval elapses. With <paramref name="immediate"/> set, the first call in a quiet
    /// window runs right away and later calls within the window only push the deadline out.
    /// </summary>
    public static void Debounce(
        this DispatcherQueueTimer timer,
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
            state.Handler = (sender, _) =>
            {
                sender.Stop();
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
        public TypedEventHandler<DispatcherQueueTimer, object>? Handler;
    }
}
