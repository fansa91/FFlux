using System;
using System.Linq;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Provides utility methods for parsing and sanitizing user supplied playback options.
/// </summary>
/// <remarks>
/// These were LibVLC command-line arguments. FFmpegInteropX consumes <c>key=value</c> pairs,
/// which <c>FFmpegMediaSourceConfigFactory</c> derives from the same token shape.
/// </remarks>
public static class PlaybackOptionHelper
{
    /// <summary>
    /// Sanitizes option tokens by trimming whitespace, filtering out non-option tokens and
    /// lone dashes, and returning a space-delimited string.
    /// </summary>
    /// <param name="raw">The raw input string of options.</param>
    /// <returns>A space-delimited string of valid option tokens.</returns>
    public static string SanitizeArguments(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        string[] args = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.StartsWith('-') && s != "--")
            .ToArray();

        return string.Join(' ', args);
    }
}
