using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Screenbox.Core.Enums;

namespace Screenbox.Core.Services;

/// <summary>
/// The outcome of classifying one network URL.
/// </summary>
/// <param name="Url">The normalized URL the result describes.</param>
/// <param name="FormatType">The detected streaming format.</param>
/// <param name="SuggestedName">A human readable default name derived from the URL.</param>
/// <param name="IsPlaylist">Whether the URL might expand into several entries.</param>
/// <param name="WasSniffed">Whether the format came from an HTTP response rather than the URL itself.</param>
public sealed record StreamDetectionResult(
    string Url,
    StreamFormatType FormatType,
    string SuggestedName,
    bool IsPlaylist = false,
    bool WasSniffed = false);

/// <summary>
/// One playable entry extracted from an extended M3U playlist.
/// </summary>
public sealed record StreamPlaylistEntry(string Name, string Url);

/// <summary>
/// Classifies network URLs into a <see cref="StreamFormatType"/> and derives default names.
/// </summary>
public interface IStreamUrlDetector
{
    /// <summary>
    /// Classifies a URL from its scheme and file extension only. Never touches the network.
    /// </summary>
    StreamDetectionResult Detect(string url);

    /// <summary>
    /// Classifies a URL, falling back to a short HTTP <c>HEAD</c> request when the URL itself
    /// carries no extension. Never throws: an unreachable host simply stays
    /// <see cref="StreamFormatType.Unknown"/>.
    /// </summary>
    Task<StreamDetectionResult> DetectAsync(
        string url,
        string? userAgent = null,
        string? referer = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an <c>.m3u</c> / <c>.m3u8</c> playlist and returns its entries. Returns an empty
    /// list for HLS manifests, unreadable URLs, or anything that is not an extended M3U list.
    /// </summary>
    Task<IReadOnlyList<StreamPlaylistEntry>> ReadPlaylistAsync(
        string url,
        string? userAgent = null,
        string? referer = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Derives a default display name from a URL: the last path segment, or the host when the
    /// path carries nothing usable.
    /// </summary>
    string SuggestName(string url);
}

/// <summary>
/// Default <see cref="IStreamUrlDetector"/> implementation.
/// </summary>
/// <remarks>
/// Detection runs in three layers, cheapest first: protocol scheme, then file extension, then a
/// 3 second HTTP probe reading <c>Content-Type</c>. The probe is only issued for extensionless
/// HTTP(S) URLs so a plain <c>.mp4</c> link never costs a round trip.
/// </remarks>
public sealed class StreamUrlDetector : IStreamUrlDetector
{
    private const string DefaultUserAgent = "FFlux/1.0 (Windows)";
    private static readonly TimeSpan s_sniffTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan s_playlistTimeout = TimeSpan.FromSeconds(8);
    private const int MaxPlaylistBytes = 512 * 1024;

    private static readonly HttpClient s_http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly HashSet<string> s_videoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".webm", ".flv", ".ts", ".m4v", ".mov", ".avi", ".wmv", ".mpg", ".mpeg",
        ".3gp", ".ogv",
    };

    private static readonly HashSet<string> s_audioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".aac", ".m4a", ".ogg", ".oga", ".opus", ".wav", ".wma", ".ape", ".alac",
    };

    /// <summary>
    /// Playlist extensions that may hold an extended M3U list. <c>.m3u8</c> is also the HLS
    /// manifest extension, so the content has to be inspected before treating it as a list.
    /// </summary>
    private static readonly HashSet<string> s_playlistExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m3u", ".m3u8",
    };

    /// <inheritdoc/>
    public StreamDetectionResult Detect(string url)
    {
        string trimmed = url?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new StreamDetectionResult(string.Empty, StreamFormatType.Unknown, string.Empty);
        }

        string scheme = GetScheme(trimmed);
        StreamFormatType protocolFormat = scheme switch
        {
            "rtsp" or "rtsps" => StreamFormatType.Rtsp,
            "rtmp" or "rtmps" or "rtmpt" or "rtmpe" => StreamFormatType.Rtmp,
            "srt" => StreamFormatType.Srt,
            "mms" or "mmsh" or "mmst" => StreamFormatType.Mms,
            _ => StreamFormatType.Unknown,
        };

        if (protocolFormat != StreamFormatType.Unknown)
        {
            return new StreamDetectionResult(trimmed, protocolFormat, SuggestName(trimmed));
        }

        string extension = GetExtension(trimmed);
        StreamFormatType format = extension.ToLowerInvariant() switch
        {
            ".m3u8" => StreamFormatType.Hls,
            ".mpd" => StreamFormatType.Dash,
            ".m3u" => StreamFormatType.Playlist,
            _ when s_videoExtensions.Contains(extension) => StreamFormatType.Video,
            _ when s_audioExtensions.Contains(extension) => StreamFormatType.Audio,
            _ => StreamFormatType.Unknown,
        };

        return new StreamDetectionResult(
            trimmed,
            format,
            SuggestName(trimmed),
            IsPlaylist: s_playlistExtensions.Contains(extension));
    }

    /// <inheritdoc/>
    public async Task<StreamDetectionResult> DetectAsync(
        string url,
        string? userAgent = null,
        string? referer = null,
        CancellationToken cancellationToken = default)
    {
        StreamDetectionResult basic = Detect(url);
        if (basic.FormatType != StreamFormatType.Unknown) return basic;
        if (!TryNormalize(basic.Url, out Uri? uri, out _)) return basic;
        if (uri.Scheme is not ("http" or "https")) return basic;

        // Only probe when the URL itself told us nothing; a content-type probe is best effort.
        StreamFormatType sniffed = await SniffAsync(uri, userAgent, referer, cancellationToken)
            .ConfigureAwait(false);
        return basic with { FormatType = sniffed, WasSniffed = true };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StreamPlaylistEntry>> ReadPlaylistAsync(
        string url,
        string? userAgent = null,
        string? referer = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalize(url?.Trim() ?? string.Empty, out Uri? uri, out _)) return [];
        if (uri.Scheme is not ("http" or "https")) return [];

        string? content = await FetchTextAsync(uri, userAgent, referer, cancellationToken)
            .ConfigureAwait(false);
        return content is null ? [] : ParseExtendedM3u(content, uri);
    }

    /// <inheritdoc/>
    public string SuggestName(string url)
    {
        string trimmed = url?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return string.Empty;
        if (!TryNormalize(trimmed, out Uri? uri, out _)) return trimmed;

        for (int i = uri.Segments.Length - 1; i >= 0; i--)
        {
            string segment = uri.Segments[i].Trim('/');
            if (segment.Length == 0) continue;

            string name = Uri.UnescapeDataString(segment);
            string extension = Path.GetExtension(name);
            // The badge already conveys the format, so "live/index.m3u8" reads better as "index".
            if (extension.Length > 0 && (s_playlistExtensions.Contains(extension) ||
                                         s_videoExtensions.Contains(extension) ||
                                         s_audioExtensions.Contains(extension)))
            {
                name = name[..^extension.Length];
            }

            if (name.Length > 0) return name;
        }

        return uri.Host.Length > 0 ? uri.Host : trimmed;
    }

    /// <summary>
    /// Repairs common paste mistakes: surrounding whitespace, a missing scheme, and the
    /// backslash-forwardslash typo some browsers produce when copying a URL.
    /// </summary>
    public static bool TryNormalize(string url, out Uri? uri, out string normalized)
    {
        normalized = url?.Trim() ?? string.Empty;
        uri = null;
        if (normalized.Length == 0) return false;

        // Local paths are not network links. They have to be rejected before any URI parsing,
        // because .NET happily turns "C:\videos\a.mkv" into "file:///C:/videos/a.mkv" and a
        // UNC share into "file://server/share" - both would silently become unplayable streams.
        if (LooksLikeLocalPath(normalized)) return false;

        // Protocol relative address. .NET reads "//host/path" as a UNC share, so it is resolved
        // here instead of falling through to the URI parser.
        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            if (Uri.TryCreate("http:" + normalized, UriKind.Absolute, out uri))
            {
                normalized = uri.OriginalString;
                return true;
            }

            uri = null;
            return false;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out uri) && uri.Scheme.Length > 1)
        {
            // A Windows path such as "C:\media\a.mkv" also parses as an absolute URI (scheme "c").
            if (uri.Scheme is not ("http" or "https" or "rtsp" or "rtsps" or "rtmp" or "rtmps"
                or "rtmpt" or "rtmpe" or "srt" or "mms" or "mmsh" or "mmst" or "ftp"))
            {
                uri = null;
                return false;
            }

            normalized = uri.OriginalString;
            return true;
        }

        // No usable scheme: assume HTTP, which is what "example.com/live.m3u8" almost always means.
        string withScheme = "http://" + normalized.TrimStart('/');
        if (Uri.TryCreate(withScheme, UriKind.Absolute, out uri))
        {
            normalized = uri.OriginalString;
            return true;
        }

        uri = null;
        return false;
    }

    /// <summary>
    /// Recognizes the shapes of a local Windows path: a drive letter ("C:\videos\a.mkv",
    /// "D:/media/a.mkv"), a UNC share ("\\server\share") and an explicit <c>file://</c> URI.
    /// </summary>
    private static bool LooksLikeLocalPath(string value)
    {
        if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return true;

        // UNC share. The forward slash form "//server/share" is left alone on purpose: in a URL
        // context it is a protocol relative address, which is more likely than a UNC path.
        if (value.StartsWith(@"\\", StringComparison.Ordinal)) return true;

        // Drive letter. "C://host" is excluded so it can still be parsed as a scheme.
        return value.Length >= 3 &&
               char.IsAsciiLetter(value[0]) &&
               value[1] == ':' &&
               value[2] is '\\' or '/' &&
               !(value.Length >= 4 && value[2] == '/' && value[3] == '/');
    }

    private static string GetScheme(string url)
    {
        int separator = url.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0) return string.Empty;
        return url[..separator].ToLowerInvariant();
    }

    private static string GetExtension(string url)
    {
        int cut = url.IndexOfAny(['?', '#']);
        string path = cut >= 0 ? url[..cut] : url;
        int slash = path.LastIndexOf('/');
        string lastSegment = slash >= 0 ? path[(slash + 1)..] : path;
        int dot = lastSegment.LastIndexOf('.');
        return dot > 0 ? lastSegment[dot..] : string.Empty;
    }

    private static async Task<StreamFormatType> SniffAsync(
        Uri uri,
        string? userAgent,
        string? referer,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource cts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(s_sniffTimeout);

        try
        {
            using HttpRequestMessage head = new(HttpMethod.Head, uri);
            ApplyRequestHeaders(head, userAgent, referer);
            using HttpResponseMessage response =
                await s_http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                    .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return FromContentType(response.Content.Headers.ContentType?.MediaType);
            }

            // 405/501 mean "HEAD unsupported", which is common for signed CDN links. Any other
            // status is a real answer, so keep the format unknown instead of hammering the host.
            if (response.StatusCode is not (HttpStatusCode.MethodNotAllowed
                or HttpStatusCode.NotImplemented))
            {
                return StreamFormatType.Unknown;
            }
        }
        catch (Exception)
        {
            // Timeout, DNS failure, TLS rejection: fall through to the ranged GET probe.
        }

        try
        {
            using CancellationTokenSource getCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            getCts.CancelAfter(s_sniffTimeout);

            using HttpRequestMessage get = new(HttpMethod.Get, uri);
            get.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            ApplyRequestHeaders(get, userAgent, referer);
            using HttpResponseMessage response =
                await s_http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, getCts.Token)
                    .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? FromContentType(response.Content.Headers.ContentType?.MediaType)
                : StreamFormatType.Unknown;
        }
        catch (Exception)
        {
            return StreamFormatType.Unknown;
        }
    }

    private static async Task<string?> FetchTextAsync(
        Uri uri,
        string? userAgent,
        string? referer,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource cts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(s_playlistTimeout);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, uri);
            ApplyRequestHeaders(request, userAgent, referer);
            using HttpResponseMessage response =
                await s_http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                    .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using Stream stream = await response.Content.ReadAsStreamAsync(cts.Token)
                .ConfigureAwait(false);

            // A playlist is text; a huge body means this is not one, so stop early rather than
            // downloading a video by accident.
            byte[] buffer = new byte[MaxPlaylistBytes];
            int total = 0;
            while (total < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(total), cts.Token)
                    .ConfigureAwait(false);
                if (read == 0) break;
                total += read;
            }

            return Encoding.UTF8.GetString(buffer, 0, total);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void ApplyRequestHeaders(
        HttpRequestMessage request,
        string? userAgent,
        string? referer)
    {
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            string.IsNullOrWhiteSpace(userAgent) ? DefaultUserAgent : userAgent.Trim());

        if (!string.IsNullOrWhiteSpace(referer) &&
            Uri.TryCreate(referer.Trim(), UriKind.Absolute, out Uri? refererUri))
        {
            request.Headers.Referrer = refererUri;
        }
    }

    private static StreamFormatType FromContentType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return StreamFormatType.Unknown;

        string type = mediaType.Split(';')[0].Trim().ToLowerInvariant();
        return type switch
        {
            "application/vnd.apple.mpegurl" or "application/x-mpegurl"
                or "audio/mpegurl" or "audio/x-mpegurl" => StreamFormatType.Hls,
            "application/dash+xml" => StreamFormatType.Dash,
            "video/mp4" or "video/x-matroska" or "video/webm" or "video/mp2t"
                or "video/x-flv" or "video/quicktime" or "video/x-msvideo" => StreamFormatType.Video,
            "audio/mpeg" or "audio/mp4" or "audio/flac" or "audio/aac" or "audio/ogg"
                or "audio/opus" or "audio/x-flac" or "audio/wav" or "audio/x-wav" => StreamFormatType.Audio,
            _ when type.StartsWith("video/", StringComparison.Ordinal) => StreamFormatType.Video,
            _ when type.StartsWith("audio/", StringComparison.Ordinal) => StreamFormatType.Audio,
            _ when type.Contains("mpegurl", StringComparison.Ordinal) => StreamFormatType.Hls,
            _ => StreamFormatType.Unknown,
        };
    }

    /// <summary>
    /// Parses an extended M3U list. HLS manifests are rejected on purpose: they carry
    /// <c>#EXT-X-</c> tags and their <c>#EXTINF</c> entries are segments of one stream, not
    /// separate channels, so importing them would fill the library with unusable fragments.
    /// </summary>
    public static IReadOnlyList<StreamPlaylistEntry> ParseExtendedM3u(string content, Uri baseUri)
    {
        if (!content.Contains("#EXTINF", StringComparison.OrdinalIgnoreCase)) return [];
        if (content.Contains("#EXT-X-", StringComparison.OrdinalIgnoreCase)) return [];

        List<StreamPlaylistEntry> entries = [];
        string? pendingTitle = null;

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = line.IndexOf(',');
                pendingTitle = comma >= 0 && comma + 1 < line.Length
                    ? line[(comma + 1)..].Trim()
                    : null;
                continue;
            }

            if (line[0] == '#') continue;

            if (!Uri.TryCreate(baseUri, line, out Uri? entryUri)) continue;
            string name = string.IsNullOrWhiteSpace(pendingTitle)
                ? Path.GetFileName(entryUri.LocalPath)
                : pendingTitle;
            entries.Add(new StreamPlaylistEntry(name, entryUri.ToString()));
            pendingTitle = null;
        }

        return entries;
    }
}
