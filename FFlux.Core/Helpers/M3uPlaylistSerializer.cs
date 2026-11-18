using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Builds Extended M3U playlists from plain path/title/duration tuples.
/// </summary>
/// <remarks>
/// Pure logic (no storage, no view models) so it can be unit tested and runs unchanged under
/// Native AOT. The caller is responsible for writing the returned string to a file.
/// </remarks>
public static class M3uPlaylistSerializer
{
    /// <summary>UTF-8 with a BOM: readers can detect it even for a .m3u file.</summary>
    public static byte[] Encode(string playlist)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        // GetBytes() never emits the preamble; prepend it so a .m3u reader using
        // detectEncodingFromByteOrderMarks picks UTF-8 instead of the local ANSI code page.
        byte[] content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(playlist);
        byte[] preamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble();
        byte[] result = new byte[preamble.Length + content.Length];
        preamble.CopyTo(result, 0);
        content.CopyTo(result, preamble.Length);
        return result;
    }

    public static string Serialize(IReadOnlyList<M3uEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        foreach (M3uEntry entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path)
                || string.Equals(entry.Path, "about:blank", StringComparison.Ordinal))
            {
                continue;
            }

            int durationSeconds = entry.Duration > TimeSpan.Zero
                ? (int)Math.Round(entry.Duration.TotalSeconds)
                : -1;

            string path = Uri.TryCreate(entry.Path, UriKind.Absolute, out Uri? uri)
                ? uri.AbsoluteUri
                : entry.Path;

            sb.Append("#EXTINF:")
              .Append(durationSeconds.ToString(CultureInfo.InvariantCulture))
              .Append(',')
              .AppendLine(entry.Title ?? string.Empty)
              .AppendLine(path);
        }

        return sb.ToString();
    }
}

/// <summary>One entry of an exported playlist.</summary>
public readonly record struct M3uEntry(string Path, string Title, TimeSpan Duration);
