using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Screenbox.Core.Models;

/// <summary>A single timed lyric line.</summary>
/// <param name="Time">When the line starts, relative to the beginning of the track.</param>
/// <param name="Text">The line text, with metadata tags already removed.</param>
public readonly record struct LyricLine(TimeSpan Time, string Text);

/// <summary>
/// Parses LRC lyric files into timed lines.
/// </summary>
/// <remarks>
/// Kept free of WinRT / storage types so it runs in the plain desktop test host and under
/// Native AOT. Supported syntax: one or more [mm:ss.xx] timestamps per line, an optional
/// [offset:+/-ms] tag (applied to every line), and ID3-style [ti:...] / [ar:...] metadata which
/// is ignored. Malformed lines are skipped rather than failing the whole file.
/// </remarks>
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\s*\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OffsetPattern();

    public static IReadOnlyList<LyricLine> Parse(string? lrcText)
    {
        if (string.IsNullOrWhiteSpace(lrcText)) return Array.Empty<LyricLine>();

        TimeSpan offset = TimeSpan.Zero;
        var lines = new List<LyricLine>();

        foreach (string rawLine in lrcText.Split('\n'))
        {
            string line = rawLine.Trim().Trim('\r');
            if (line.Length == 0) continue;

            Match offsetMatch = OffsetPattern().Match(line);
            if (offsetMatch.Success
                && int.TryParse(offsetMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetMs))
            {
                offset = TimeSpan.FromMilliseconds(offsetMs);
                continue;
            }

            MatchCollection timestamps = TimestampPattern().Matches(line);
            if (timestamps.Count == 0) continue;

            // Everything after the last timestamp is the text. An earlier timestamp in the same
            // line is a repeat (common in choruses), so emit one entry per timestamp.
            string text = line[(timestamps[^1].Index + timestamps[^1].Length)..].Trim();
            if (text.Length == 0) continue;

            foreach (Match timestamp in timestamps)
            {
                if (!TryParseTimestamp(timestamp, out TimeSpan time)) continue;
                lines.Add(new LyricLine(time + offset, text));
            }
        }

        if (lines.Count == 0) return Array.Empty<LyricLine>();

        // A file can contain lines out of order; highlight search needs a sorted sequence.
        lines.Sort(static (a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    private static bool TryParseTimestamp(Match match, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
        {
            return false;
        }

        double fraction = 0;
        if (match.Groups[3].Success)
        {
            string digits = match.Groups[3].Value;
            // LRC uses hundredths (two digits) but players in the wild emit one or three.
            double scale = digits.Length switch { 1 => 0.1, 2 => 0.01, _ => 0.001 };
            fraction = double.Parse(digits, CultureInfo.InvariantCulture) * scale;
        }

        time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds + fraction);
        return true;
    }

    /// <summary>Returns the index of the last line whose time is at or before <paramref name="position"/>.</summary>
    public static int FindLineIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        if (lines.Count == 0) return -1;

        int low = 0;
        int high = lines.Count - 1;
        int result = -1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            if (lines[mid].Time <= position)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }
}
