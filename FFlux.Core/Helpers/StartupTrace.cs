using System;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Stage markers for startup diagnosis.
/// </summary>
/// <remarks>
/// <para>
/// Core cannot reference the app's <c>StartupErrorLog</c>, so the host installs a sink once at
/// startup. Until then every call is a no-op, which keeps the Core unit tests silent and means
/// adding a marker cannot change behaviour on its own.
/// </para>
/// <para>
/// The sink is a synchronous append of one short line, and markers are written a handful of times
/// per scan (never per file), so the cost is a few microseconds at points that already wait on IO.
/// </para>
/// </remarks>
public static class StartupTrace
{
    private static Action<string>? _sink;

    /// <summary>Installs the process-wide sink, or clears it when <paramref name="sink"/> is null.</summary>
    public static void SetSink(Action<string>? sink) => _sink = sink;

    public static void Write(string message)
    {
        Action<string>? sink = _sink;
        if (sink is null) return;

        try
        {
            sink(message);
        }
        catch (Exception)
        {
            // Diagnostics must never mask the original failure.
        }
    }

    public static void Write(string message, Exception exception)
    {
        // HResult first: it is the only part that identifies a WinRT/native failure.
        Write($"{message} hr=0x{exception.HResult:X8} {exception.GetType().Name}: {exception.Message}");
    }
}
