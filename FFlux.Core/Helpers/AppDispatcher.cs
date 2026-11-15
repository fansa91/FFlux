using System;
using Windows.Foundation;
using WinUIDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using WinUIDispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using WinUIDispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Single access point for UI-thread dispatching.
///
/// Production code uses <see cref="Current"/> without ever naming a WinRT
/// dispatcher type. A test host (or any other host without a WinUI XAML
/// application context) calls <see cref="SetProvider"/> once during startup;
/// after that the WinUI-backed implementation is never constructed, so no
/// activation factory is touched.
/// </summary>
public static class AppDispatcher
{
    private static IAppDispatcher? _provider;

    private static readonly Lazy<IAppDispatcher> WinUIDefault = new(static () =>
    {
        WinUIDispatcherQueue? queue = WinUIDispatcherQueue.GetForCurrentThread();
        if (queue is null)
        {
            throw new InvalidOperationException(
                "No WinUI DispatcherQueue is available on the current thread. " +
                "Call AppDispatcher.SetProvider(...) when hosting Core outside a WinUI XAML application.");
        }

        return new WinUIAppDispatcher(queue);
    });

    /// <summary>The dispatcher for the current host.</summary>
    public static IAppDispatcher Current => _provider ?? WinUIDefault.Value;

    /// <summary>
    /// Overrides the dispatcher implementation. Intended for test hosts and
    /// other non-XAML hosts; call before any ViewModel or coordinator is built.
    /// </summary>
    public static void SetProvider(IAppDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _provider = dispatcher;
    }

    /// <summary>Restores the WinUI-backed default (mainly for test isolation).</summary>
    public static void Reset() => _provider = null;

    private sealed class WinUIAppDispatcher(WinUIDispatcherQueue queue) : IAppDispatcher
    {
        public bool HasThreadAccess => queue.HasThreadAccess;

        public bool TryEnqueue(Action action) =>
            queue.TryEnqueue(() => action());

        public bool TryEnqueue(AppDispatcherPriority priority, Action action) =>
            queue.TryEnqueue(ToWinUIPriority(priority), () => action());

        public IAppDispatcherTimer CreateTimer() =>
            new WinUIAppDispatcherTimer(queue.CreateTimer());

        private static WinUIDispatcherQueuePriority ToWinUIPriority(AppDispatcherPriority priority) =>
            priority switch
            {
                AppDispatcherPriority.Low => WinUIDispatcherQueuePriority.Low,
                AppDispatcherPriority.High => WinUIDispatcherQueuePriority.High,
                _ => WinUIDispatcherQueuePriority.Normal
            };
    }

    private sealed class WinUIAppDispatcherTimer : IAppDispatcherTimer
    {
        private readonly WinUIDispatcherQueueTimer _timer;
        private EventHandler? _tick;

        public WinUIAppDispatcherTimer(WinUIDispatcherQueueTimer timer)
        {
            _timer = timer;
            _timer.Tick += OnNativeTick;
        }

        public TimeSpan Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        public bool IsRepeating
        {
            get => _timer.IsRepeating;
            set => _timer.IsRepeating = value;
        }

        public bool IsRunning => _timer.IsRunning;

        public event EventHandler? Tick
        {
            add => _tick += value;
            remove => _tick -= value;
        }

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        private void OnNativeTick(WinUIDispatcherQueueTimer sender, object args) =>
            _tick?.Invoke(this, EventArgs.Empty);
    }
}
