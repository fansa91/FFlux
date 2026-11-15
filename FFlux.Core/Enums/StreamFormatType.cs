using System.Text.Json.Serialization;

namespace Screenbox.Core.Enums;

/// <summary>
/// The streaming format of a network URL, as detected by
/// <see cref="Services.IStreamUrlDetector"/>.
/// </summary>
/// <remarks>
/// This is a display-oriented classification: it drives the badge text shown next to a saved
/// stream, so it deliberately merges every progressive video container into one
/// <see cref="Video"/> value and every progressive audio container into <see cref="Audio"/>.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<StreamFormatType>))]
public enum StreamFormatType
{
    /// <summary>The format could not be determined from the URL or the response headers.</summary>
    Unknown,

    /// <summary>HTTP Live Streaming (<c>.m3u8</c>, <c>application/vnd.apple.mpegurl</c>).</summary>
    Hls,

    /// <summary>MPEG-DASH (<c>.mpd</c>, <c>application/dash+xml</c>).</summary>
    Dash,

    /// <summary>A progressive video file served over HTTP (<c>.mp4</c>, <c>.mkv</c>, ...).</summary>
    Video,

    /// <summary>A progressive audio file served over HTTP (<c>.mp3</c>, <c>.flac</c>, ...).</summary>
    Audio,

    /// <summary>Real Time Streaming Protocol.</summary>
    Rtsp,

    /// <summary>Real Time Messaging Protocol.</summary>
    Rtmp,

    /// <summary>Secure Reliable Transport.</summary>
    Srt,

    /// <summary>Microsoft Media Server.</summary>
    Mms,

    /// <summary>An extended M3U playlist that points at further media items.</summary>
    Playlist,
}
