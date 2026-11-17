using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Tests;

public class LibraryParseGuardTests
{
    private static readonly NullLogger Logger = NullLogger.Instance;

    [Test]
    public async Task TryParseAsync_WhenParseSucceeds_ShouldReturnTrue()
    {
        bool ran = false;

        bool parsed = await LibraryParseGuard.TryParseAsync(
            () => { ran = true; return Task.CompletedTask; },
            Logger, @"C:\Music\song.mp3");

        await Assert.That(parsed).IsTrue();
        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task TryParseAsync_WhenParseThrows_ShouldReturnFalse_AndNotEscalate()
    {
        // A corrupt container / FFmpeg COM error must be contained, matching the real scan path.
        bool parsed = await LibraryParseGuard.TryParseAsync(
            () => throw new InvalidOperationException("bad container"),
            Logger, @"C:\Music\corrupt.mp3");

        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParseAsync_WhenCancelled_ShouldRethrowOperationCancelled()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        bool escaped = false;
        try
        {
            await LibraryParseGuard.TryParseAsync(
                () => throw new OperationCanceledException(cts.Token),
                Logger, @"C:\Music\song.mp3", cts.Token);
        }
        catch (OperationCanceledException)
        {
            escaped = true;
        }

        // Cancellation is not a bad file: it has to unwind the whole batch.
        await Assert.That(escaped).IsTrue();
    }

    [Test]
    public async Task TryParseAsync_WhenReporterWouldThrow_ShouldNotEscape()
    {
        // Even a logging failure must not turn into an escaping exception on the scan path.
        bool parsed = await LibraryParseGuard.TryParseAsync(
            () => throw new InvalidOperationException("bad container"),
            new ThrowingLogger(), @"C:\Music\corrupt.mp3");

        await Assert.That(parsed).IsFalse();
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopScope();
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new InvalidOperationException("logger failed");

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
