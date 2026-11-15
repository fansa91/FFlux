using System;
using System.IO;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Opt-in playback diagnostics. The FFmpeg source is created on an async WinRT
/// path whose failures are otherwise swallowed by AttachSourceAsync, so a small
/// append-only trace makes "did FFmpeg actually decode this file" answerable.
/// Enable with FFLUX_PLAYBACK_TRACE=1 (FFLUX_STARTUP_TRACE=1 also works).
/// </summary>
public static class PlaybackTrace
{
    private static readonly object Gate = new();

    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("FFLUX_PLAYBACK_TRACE") == "1" ||
        Environment.GetEnvironmentVariable("FFLUX_STARTUP_TRACE") == "1" ||
        Environment.GetEnvironmentVariable("FFLUX_PLAYBACK_PROBE") == "1";

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDataMigration.CurrentFolderName, "playback-trace.log");

    public static void Write(string message)
    {
        if (!Enabled) return;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Diagnostics must never mask the original failure.
        }
    }

    public static void Write(string message, Exception exception) =>
        Write(message + " " + exception.GetType().Name + " hr=0x" +
              exception.HResult.ToString("X8") + ": " + exception.Message);
}
