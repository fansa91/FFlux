using System;
using System.Collections.Generic;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Tests.Helpers;

/// <summary>
/// Deterministic, fully managed <see cref="IAppDispatcher"/> for the plain
/// desktop test host.
///
/// Faithful to the real WinUI queue: <c>TryEnqueue</c> only <em>posts</em> work,
/// it does not run it inline. A test drives the queue explicitly with
/// <see cref="Flush"/>, and timers only tick via
/// <see cref="TestAppDispatcherTimer.Fire"/>. This keeps ViewModel tests free of
/// WinRT activation, timing races, and cross-test messenger cross-talk (several
/// ViewModels stay registered on the global messenger after their test ends).
/// </summary>
public sealed class TestAppDispatcher : IAppDispatcher
{
    private readonly Queue<Action> _pending = new();
    private readonly object _gate = new();

    public static TestAppDispatcher Instance { get; } = new();

    public int EnqueuedCount { get; private set; }

    /// <summary>
    /// Always false so <c>TryExecuteOrEnqueue</c> also takes the posting path,
    /// matching what a background thread sees in the real app.
    /// </summary>
    public bool HasThreadAccess => false;

    public int PendingCount
    {
        get { lock (_gate) { return _pending.Count; } }
    }

    public bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            _pending.Enqueue(action);
            EnqueuedCount++;
        }
        return true;
    }

    public bool TryEnqueue(AppDispatcherPriority priority, Action action) => TryEnqueue(action);

    /// <summary>Runs every posted callback. Callbacks posted while flushing are drained too.</summary>
    public void Flush()
    {
        while (true)
        {
            Action? next;
            lock (_gate)
            {
                if (_pending.Count == 0) return;
                next = _pending.Dequeue();
            }
            next();
        }
    }

    /// <summary>Discards queued work; used to isolate tests from each other.</summary>
    public void Clear()
    {
        lock (_gate) { _pending.Clear(); }
    }

    public IAppDispatcherTimer CreateTimer() => new TestAppDispatcherTimer();

    public static void Install()
    {
        Instance.Clear();
        AppDispatcher.SetProvider(Instance);
    }
}

public sealed class TestAppDispatcherTimer : IAppDispatcherTimer
{
    public TimeSpan Interval { get; set; }
    public bool IsRepeating { get; set; } = true;
    public bool IsRunning { get; private set; }

    public event EventHandler? Tick;

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    /// <summary>Simulates one tick. No-op when the timer is not running.</summary>
    public void Fire()
    {
        if (IsRunning)
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}
