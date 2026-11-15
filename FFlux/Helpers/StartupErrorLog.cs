using System;
using System.IO;

namespace Screenbox.Helpers;

/// <summary>
/// Append-only startup diagnostics for an unpackaged app. There is no debugger output
/// window and no WER report for a WinExe launched from the command line, so every
/// startup stage and unhandled exception is persisted next to the user data folder.
/// </summary>
public static class StartupErrorLog
{
    private static readonly object Gate = new();

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFlux", "startup-error.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Diagnostics must never mask the original failure.
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
}
