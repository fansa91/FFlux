using System;
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
}
