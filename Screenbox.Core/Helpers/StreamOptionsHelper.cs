using System;
using System.Collections.Generic;
using System.Text;
using Screenbox.Core.Models;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Translates the library's anti-hotlink fields into FFmpeg AVFormat options.
/// </summary>
/// <remarks>
/// FFmpeg reads these names from the option dictionary handed to <c>avformat_open_input</c> and
/// forwards the ones it does not recognise down to the protocol layer, so <c>user_agent</c>,
/// <c>referer</c> and <c>headers</c> reach the HTTP/HTTPS handler.
/// </remarks>
public static class StreamOptionsHelper
{
    /// <summary>
    /// Builds the FFmpeg option list for a saved stream. Returns an empty array when the entry
    /// carries no anti-hotlink configuration.
    /// </summary>
    public static string[] ToFFmpegOptions(StreamItem? item) => item is null
        ? []
        : ToFFmpegOptions(item.UserAgent, item.Referer, item.Headers);

    /// <summary>
    /// Builds the FFmpeg option list from raw field values.
    /// </summary>
    public static string[] ToFFmpegOptions(string? userAgent, string? referer, string? headers)
    {
        List<string> options = [];

        if (userAgent?.Trim() is { Length: > 0 } agent)
        {
            options.Add($"user_agent={agent}");
        }

        if (referer?.Trim() is { Length: > 0 } reference)
        {
            options.Add($"referer={reference}");
        }

        if (NormalizeHeaders(headers) is { Length: > 0 } headerBlock)
        {
            options.Add($"headers={headerBlock}");
        }

        return options.ToArray();
    }

    /// <summary>
    /// Normalizes user supplied headers into the form FFmpeg expects: CRLF separated
    /// <c>Name: Value</c> pairs.
    /// </summary>
    /// <remarks>
    /// Lines without a colon are dropped rather than forwarded, because FFmpeg aborts the open
    /// with an unhelpful "Invalid argument" when a header line cannot be parsed.
    /// </remarks>
    public static string NormalizeHeaders(string? headers)
    {
        if (string.IsNullOrWhiteSpace(headers)) return string.Empty;

        StringBuilder builder = new();
        foreach (string rawLine in headers.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;

            int colon = line.IndexOf(':');
            if (colon <= 0 || colon == line.Length - 1) continue;

            string name = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (name.Length == 0 || value.Length == 0) continue;

            if (builder.Length > 0) builder.Append("\r\n");
            builder.Append(name).Append(": ").Append(value);
        }

        return builder.ToString();
    }
}
