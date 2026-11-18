using System;
using System.Collections.Generic;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Extracts embedded (in-tag) lyrics text from FFmpeg metadata tags.
/// </summary>
/// <remarks>
/// Kept free of WinRT and FFmpeg types so it can be unit tested in the plain desktop host and
/// trimmed safely under Native AOT. Only text is returned; timestamp parsing stays in
/// <see cref="Models.LrcParser"/>, which means untagged/plain-text lyrics naturally produce no
/// timed lines and the caller can fall back to a sidecar .lrc file.
///
/// FFNormalisation by container:
/// - MP3 / ID3v2 USLT: "lyrics" (sometimes "lyrics-eng" or "USLT")
/// - FLAC / Ogg Vorbis comments: "LYRICS", "UNSYNCEDLYRICS"
/// - MP4 / M4A "©lyr" atom: "lyrics"
/// - WMA / ASF: "WM/Lyrics"
/// </remarks>
public static class EmbeddedLyricsReader
{
    /// <summary>
    /// Picks the best lyric-bearing value from the supplied metadata tags.
    /// </summary>
    /// <param name="tags">Flattened tag key/value pairs from FFmpeg metadata.</param>
    /// <returns>The lyric text, or <see langword="null"/> when no lyric tag carries text.</returns>
    public static string? FindLyricsText(IEnumerable<KeyValuePair<string, string>> tags)
    {
        if (tags is null) throw new ArgumentNullException(nameof(tags));

        string? bestKey = null;
        string? bestText = null;
        int bestScore = int.MaxValue;

        foreach (KeyValuePair<string, string> tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Value)) continue;

            int score = ScoreKey(tag.Key);
            if (score < 0 || score > bestScore) continue;

            if (score < bestScore)
            {
                bestKey = tag.Key;
                bestText = tag.Value;
                bestScore = score;
            }
            else if (string.Equals(bestKey, tag.Key, StringComparison.OrdinalIgnoreCase))
            {
                // Two values under the same key are two lyric chunks; keep their order.
                bestText += "\n" + tag.Value;
            }
        }

        return string.IsNullOrWhiteSpace(bestText) ? null : bestText;
    }

    /// <summary>
    /// Returns a lower score for a more specific lyric key, or -1 when the key is unrelated.
    /// </summary>
    private static int ScoreKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return -1;

        string normalized = key.Trim().ToLowerInvariant().TrimStart('\uFEFF');
        return normalized switch
        {
            "lyrics" or "lyric" or "lyr" or "\u00a9lyr" => 0,
            "unsyncedlyrics" or "uslt" or "wm/lyrics" or "unsyncedlyric" => 1,
            _ when IsLanguageVariant(normalized) => 2,
            _ when normalized.Contains("lyric", StringComparison.Ordinal) => 3,
            _ => -1
        };
    }

    private static bool IsLanguageVariant(string normalized)
    {
        // "lyrics-eng", "lyrics:chi", "lyrics_zh" and similar language/description suffixes.
        foreach (string prefix in new[] { "lyrics-", "lyrics:", "lyrics_", "unsyncedlyrics-" })
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal)
                && normalized.Length > prefix.Length)
            {
                return true;
            }
        }

        return false;
    }
}
