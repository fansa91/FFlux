using System;
using System.Collections.Generic;
using Screenbox.Core.Helpers;
using Screenbox.Core.Tests.Helpers;

namespace Screenbox.Core.Tests;

public class AppDispatcherTests
{
    [Test]
    public async Task Current_ShouldBeTheManagedProvider_InTestHost()
    {
        IAppDispatcher dispatcher = AppDispatcher.Current;
        await Assert.That(dispatcher).IsNotNull();
        await Assert.That(dispatcher is TestAppDispatcher).IsTrue();

        IAppDispatcherTimer timer = dispatcher.CreateTimer();
        await Assert.That(timer).IsNotNull();
    }

    [Test]
    public async Task TryEnqueue_ShouldPostWork_UntilFlushed()
    {
        // Use a private instance so parallel tests cannot affect the queue.
        var dispatcher = new TestAppDispatcher();
        bool ran = false;

        bool accepted = dispatcher.TryEnqueue(() => ran = true);
        await Assert.That(accepted).IsTrue();
        await Assert.That(ran).IsFalse();
        await Assert.That(dispatcher.PendingCount).IsEqualTo(1);

        dispatcher.Flush();
        await Assert.That(ran).IsTrue();
        await Assert.That(dispatcher.PendingCount).IsEqualTo(0);
    }

    [Test]
    public async Task TryEnqueue_WithPriority_ShouldStillPost()
    {
        var dispatcher = new TestAppDispatcher();
        bool ran = false;

        dispatcher.TryEnqueue(AppDispatcherPriority.Low, () => ran = true);
        await Assert.That(ran).IsFalse();

        dispatcher.Flush();
        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task TryExecuteOrEnqueue_ShouldPost_WhenNotOnUiThread()
    {
        var dispatcher = new TestAppDispatcher();
        bool ran = false;

        bool accepted = dispatcher.TryExecuteOrEnqueue(() => ran = true);
        await Assert.That(accepted).IsTrue();
        await Assert.That(ran).IsFalse();

        dispatcher.Flush();
        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task DebounceCore_ShouldRunOnce_WhenTimerFires()
    {
        var timer = new TestAppDispatcherTimer();
        int calls = 0;

        timer.DebounceCore(() => calls++, TimeSpan.FromMilliseconds(50));
        await Assert.That(calls).IsEqualTo(0);
        await Assert.That(timer.IsRunning).IsTrue();

        timer.Fire();
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(timer.IsRunning).IsFalse();
    }

    [Test]
    public async Task DebounceCore_WithImmediate_ShouldRunFirstCallInline()
    {
        var timer = new TestAppDispatcherTimer();
        int calls = 0;

        timer.DebounceCore(() => calls++, TimeSpan.FromMilliseconds(50), immediate: true);
        await Assert.That(calls).IsEqualTo(1);

        // A later call inside the quiet window only pushes the deadline out.
        timer.DebounceCore(() => calls++, TimeSpan.FromMilliseconds(50), immediate: true);
        await Assert.That(calls).IsEqualTo(1);

        timer.Fire();
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task TryExecuteGuarded_ShouldPostWork_WhenNotOnUiThread()
    {
        var dispatcher = new TestAppDispatcher();
        bool ran = false;

        bool accepted = dispatcher.TryExecuteGuarded(() => ran = true);
        await Assert.That(accepted).IsTrue();
        await Assert.That(ran).IsFalse();

        dispatcher.Flush();
        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task TryExecuteGuarded_ShouldContainCallbackException_AndKeepTheQueueAlive()
    {
        var dispatcher = new TestAppDispatcher();
        var errors = new List<Exception>();
        bool continued = false;

        // Mirrors an SMTC / MediaPlayer COM callback that throws on an MTA worker thread.
        dispatcher.TryExecuteGuarded(
            () => throw new InvalidOperationException("COM callback failed"),
            errors.Add);
        dispatcher.TryExecuteGuarded(() => continued = true, errors.Add);

        // Flush must not rethrow: an escaping exception here would reach CoreMessaging and
        // terminate the process as a stowed exception (0xC000027B).
        dispatcher.Flush();

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).IsEqualTo("COM callback failed");
        await Assert.That(continued).IsTrue();
    }

    [Test]
    public async Task TryExecuteGuarded_ShouldContainReportingFailure()
    {
        var dispatcher = new TestAppDispatcher();

        dispatcher.TryExecuteGuarded(
            () => throw new InvalidOperationException("callback"),
            _ => throw new InvalidOperationException("reporter"));
        dispatcher.Flush();

        await Assert.That(dispatcher.PendingCount).IsEqualTo(0);
    }

    [Test]
    public async Task TryExecuteGuarded_ShouldRunInline_AndContainException_OnUiThread()
    {
        var dispatcher = new InlineDispatcher();
        var errors = new List<Exception>();
        bool ran = false;

        bool accepted = dispatcher.TryExecuteGuarded(
            () =>
            {
                ran = true;
                throw new InvalidOperationException("inline callback failed");
            },
            errors.Add);

        await Assert.That(accepted).IsTrue();
        await Assert.That(ran).IsTrue();
        await Assert.That(errors.Count).IsEqualTo(1);
    }

    private sealed class InlineDispatcher : IAppDispatcher
    {
        public bool HasThreadAccess => true;
        public bool TryEnqueue(Action action) { action(); return true; }
        public bool TryEnqueue(AppDispatcherPriority priority, Action action) => TryEnqueue(action);
        public IAppDispatcherTimer CreateTimer() => new TestAppDispatcherTimer();
    }
}
