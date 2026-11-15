using System;
using System.IO;
using System.Text;

namespace Screenbox.Helpers;

/// <summary>
/// Append-only startup diagnostics for an unpackaged app. There is no debugger output
/// window and no WER report for a WinExe launched from the command line, so every
/// startup stage and unhandled exception is persisted next to the user data folder.
/// </summary>
/// <remarks>
/// The heartbeat and first-chance hooks write on hot paths. The previous implementation called
/// <c>File.AppendAllText</c> for every line, which opens and closes the file (and re-creates the
/// directory) each time. One append-mode writer is kept open instead, with AutoFlush so a hard
/// crash still leaves every line on disk.
/// </remarks>
public static class StartupErrorLog
{
    private const long MaxLogBytes = 2 * 1024 * 1024;

    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFlux", "startup-error.log");
    private static readonly string LogDirectory = Path.GetDirectoryName(LogPath)!;

    private static bool _directoryReady;
    private static StreamWriter? _writer;

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                EnsureWriter();
                _writer?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
            catch (Exception)
            {
                // Diagnostics must never mask the original failure. Drop the writer so the next
                // line can try again (for example after the file is replaced by the user).
                InvalidateWriter();
            }
        }
    }

    public static void Write(Exception exception)
    {
        Write(exception.ToString());
        for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            Write("  --- Inner exception ---");
            Write(inner.ToString());
        }
    }

    /// <summary>Flushes the tail of the log on a normal process exit.</summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            try
            {
                _writer?.Flush();
            }
            catch (Exception)
            {
                // Nothing useful to do while tearing down.
            }

            InvalidateWriter();
        }
    }

    private static void EnsureWriter()
    {
        if (_writer is not null) return;

        if (!_directoryReady)
        {
            Directory.CreateDirectory(LogDirectory);
            _directoryReady = true;
        }

        try
        {
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > MaxLogBytes)
            {
                // Keep one rotation only; the log is a diagnostic aid, not an audit trail.
                File.Move(LogPath, LogPath + ".1", overwrite: true);
            }
        }
        catch (Exception)
        {
            // A locked log file must not stop the app from starting; just keep appending.
        }

        var stream = new FileStream(
            LogPath, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    private static void InvalidateWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (Exception)
        {
            // Best effort.
        }

        _writer = null;
    }
}
