using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Screenbox.Core.Helpers;
using TUnit.Core;

namespace Screenbox.Core.Tests;

// All three cases install/reset the process-wide error sink. Running them concurrently makes
// one case clear another's handler before its background task has reported the failure.
[NotInParallel]
public class FireAndForgetTests
{
    [Test]
    public async Task Run_ShouldObserveAnException_InsteadOfEscapingIt()
    {
        string context = Guid.NewGuid().ToString("N");
        var errors = new List<Exception>();
        FireAndForget.SetErrorHandler((ctx, ex) =>
        {
            if (ctx != context) return;
            lock (errors) errors.Add(ex);
        });

        try
        {
            FireAndForget.Run(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("background callback failed");
            }, context);

            for (int i = 0; i < 100 && errors.Count == 0; i++)
            {
                await Task.Delay(10);
            }

            await Assert.That(errors.Count).IsEqualTo(1);
            await Assert.That(errors[0].Message).IsEqualTo("background callback failed");
        }
        finally
        {
            FireAndForget.ResetErrorHandler();
        }
    }

    [Test]
    public async Task Run_ShouldCompleteNormalWork()
    {
        bool ran = false;
        FireAndForget.Run(async () =>
        {
            await Task.Yield();
            ran = true;
        }, "normal-work");

        for (int i = 0; i < 100 && !ran; i++)
        {
            await Task.Delay(10);
        }

        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task Report_ShouldNeverThrow_WhenTheSinkThrows()
    {
        FireAndForget.SetErrorHandler((_, _) => throw new InvalidOperationException("sink failed"));
        try
        {
            bool threw = false;
            try
            {
                FireAndForget.Report("context", new InvalidOperationException("original"));
            }
            catch (Exception)
            {
                threw = true;
            }

            await Assert.That(threw).IsFalse();
        }
        finally
        {
            FireAndForget.ResetErrorHandler();
        }
    }
}
