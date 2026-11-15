using Screenbox.Core.Enums;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Display helpers for <see cref="StreamFormatType"/>.
/// </summary>
public static class StreamFormatExtensions
{
    /// <summary>
    /// Gets the short uppercase label shown on a stream badge, e.g. <c>HLS</c>, <c>MP4</c>,
    /// <c>AUDIO</c>.
    /// </summary>
    /// <remarks>
    /// Progressive containers collapse into <c>MP4</c> / <c>AUDIO</c> on purpose: the badge is a
    /// hint at the playback pipeline rather than an exact container name, and the real container
    /// is reported by FFmpeg once the stream opens.
    /// </remarks>
    public static string ToBadgeText(this StreamFormatType format) => format switch
    {
        StreamFormatType.Hls => "HLS",
        StreamFormatType.Dash => "DASH",
        StreamFormatType.Video => "MP4",
        StreamFormatType.Audio => "AUDIO",
        StreamFormatType.Rtsp => "RTSP",
        StreamFormatType.Rtmp => "RTMP",
        StreamFormatType.Srt => "SRT",
        StreamFormatType.Mms => "MMS",
        StreamFormatType.Playlist => "M3U",
        _ => "URL",
    };

    /// <summary>
    /// Gets whether the format describes a playlist that can be expanded into several entries.
    /// </summary>
    public static bool IsExpandable(this StreamFormatType format) =>
        format is StreamFormatType.Playlist;
}
