using System;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Platform-neutral priority for work posted to the UI dispatcher.
/// </summary>
public enum AppDispatcherPriority
{
    Low,
    Normal,
    High
}

/// <summary>
/// Abstraction over the UI thread's dispatcher queue.
///
/// In the packaged/unpackaged WinUI 3 app this is backed by
/// <c>Microsoft.UI.Dispatching.DispatcherQueue</c>. That type is a WinRT
/// activation-factory call, which fails with REGDB_E_CLASSNOTREG in a plain
/// desktop test host (or any process without a WinUI XAML application context).
/// Business logic must depend on this interface instead so it stays testable.
/// </summary>
public interface IAppDispatcher
{
    /// <summary>True when the caller is already running on the UI thread.</summary>
    bool HasThreadAccess { get; }

    /// <summary>Posts <paramref name="action"/> to the UI thread.</summary>
    bool TryEnqueue(Action action);

    /// <summary>Posts <paramref name="action"/> with an explicit priority.</summary>
    bool TryEnqueue(AppDispatcherPriority priority, Action action);

    /// <summary>Creates a timer whose ticks are raised on the UI thread.</summary>
    IAppDispatcherTimer CreateTimer();
}

/// <summary>
/// Abstraction over a dispatcher-queue timer. Implementations must raise
/// <see cref="Tick"/> on the dispatcher thread (or, in tests, synchronously).
/// </summary>
public interface IAppDispatcherTimer
{
    TimeSpan Interval { get; set; }
    bool IsRepeating { get; set; }
    bool IsRunning { get; }

    event EventHandler? Tick;

    void Start();
    void Stop();
}
