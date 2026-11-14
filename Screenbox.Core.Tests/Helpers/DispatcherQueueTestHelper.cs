using System.Runtime.InteropServices;
using Windows.System;

namespace Screenbox.Core.Tests.Helpers;

public static partial class DispatcherQueueTestHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        out nint dispatcherQueueController);

    public static void EnsureDispatcherQueue()
    {
        if (DispatcherQueue.GetForCurrentThread() is not null)
        {
            return;
        }

        var options = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2, // DQTYPE_THREAD_CURRENT
            apartmentType = 2 // DQTAT_COM_STA
        };

        var hr = CreateDispatcherQueueController(options, out _);
        Marshal.ThrowExceptionForHR(hr);

        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue is not null)
        {
            // The UWP Windows.System.DispatcherQueueSynchronizationContext type is
            // not part of the WinUI 3 SDK projection. Post continuations onto the
            // queue directly so await still resumes on the dispatcher thread.
            SynchronizationContext.SetSynchronizationContext(new QueueSynchronizationContext(queue));
        }
    }

    private sealed class QueueSynchronizationContext(DispatcherQueue queue) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            queue.TryEnqueue(() => d(state));
    }
}
