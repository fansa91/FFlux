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
    private static readonly object DefaultLock = new();
    private static IAppDispatcher? _provider;
    private static IAppDispatcher? _default;

    /// <summary>The dispatcher for the current host.</summary>
    public static IAppDispatcher Current => _provider ?? GetOrCreateDefault();

    /// <summary>
    /// Resolves and pins the WinUI dispatcher on the calling (UI) thread. Call once from the
    /// application constructor, before any background / COM callback can be the first caller of
    /// <see cref="Current"/>.
    /// </summary>
    /// <remarks>
    /// A <c>Lazy&lt;T&gt;</c> used to back this lookup. Its factory throws when the first caller
    /// happens to be a thread-pool thread (no DispatcherQueue there), and Lazy permanently caches
    /// that failure - so one early background callback would make every later UI-thread lookup
    /// throw for the rest of the process. The explicit lock below never caches a failed lookup.
    /// </remarks>
    public static void InitializeForCurrentThread() => _ = GetOrCreateDefault();

    private static IAppDispatcher GetOrCreateDefault()
    {
        lock (DefaultLock)
        {
            if (_default is not null) return _default;

            WinUIDispatcherQueue? queue = WinUIDispatcherQueue.GetForCurrentThread();
            if (queue is null)
            {
                // Deliberately not cached: this call is allowed to fail on a worker thread, and
                // the next call from the real UI thread must still be able to succeed.
                throw new InvalidOperationException(
                    "No WinUI DispatcherQueue is available on the current thread. " +
                    "Call AppDispatcher.SetProvider(...) when hosting Core outside a WinUI XAML application, " +
                    "or AppDispatcher.InitializeForCurrentThread() on the UI thread at startup.");
            }

            _default = new WinUIAppDispatcher(queue);
            return _default;
        }
    }

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
    public static void Reset()
    {
        lock (DefaultLock)
        {
            _provider = null;
            _default = null;
        }
    }

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
