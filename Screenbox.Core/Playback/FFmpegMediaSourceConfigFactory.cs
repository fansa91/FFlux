using System.Collections.Generic;
using FFmpegInteropX;
using Windows.Foundation.Collections;

namespace Screenbox.Core.Playback;

/// <summary>
/// Builds the <see cref="MediaSourceConfig"/> used by the FFmpegInteropX playback backend.
/// </summary>
/// <remarks>
/// FFmpegInteropX 2.x replaced the flat <c>FastSeek</c> / <c>DecoderThreads</c> properties with
/// nested <see cref="GeneralConfig"/>, <see cref="AudioConfig"/> and <see cref="VideoConfig"/>
/// objects. Kept in one place so the hardware-decoding policy can be tuned without touching
/// the player implementation.
/// </remarks>
public static class FFmpegMediaSourceConfigFactory
{
    /// <summary>
    /// Creates a configuration with D3D11VA hardware decoding, fast seeking and automatic
    /// decoder thread counts enabled.
    /// </summary>
    /// <param name="options">
    /// Optional user supplied FFmpeg options, in <c>--key=value</c>, <c>-key=value</c> or
    /// <c>key=value</c> form (previously LibVLC command-line arguments).
    /// </param>
    /// <returns>A ready to use <see cref="MediaSourceConfig"/>.</returns>
    public static MediaSourceConfig Create(IEnumerable<string>? options = null)
    {
        MediaSourceConfig config = new();

        // VideoDecoderMode.Automatic is the FFmpegInteropX D3D11VA path: it uses the
        // FFmpeg D3D11 hardware decoder when the format/GPU supports it and transparently
        // falls back to the FFmpeg software decoder otherwise.
        config.Video.VideoDecoderMode = VideoDecoderMode.Automatic;

        // FastSeek jumps to the nearest keyframe. FFmpegMediaSource.PlaybackSession must be
        // assigned to MediaPlayer.PlaybackSession for this to take effect; FFmpegMediaPlayer
        // does that as soon as the source is opened.
        config.General.FastSeek = true;
        config.General.FastSeekCleanAudio = true;

        // 0 means "use the number of logical CPU cores" in FFmpegInteropX.
        config.Video.MaxDecoderThreads = 0;
        config.Audio.MaxDecoderThreads = 0;

        // Screenbox exposes playback rates up to 4x; the MediaStreamSource must advertise it.
        config.General.MaxSupportedPlaybackRate = 4.0;

        // Network sources should recover from transient disconnects instead of failing playback.
        PropertySet ffmpegOptions = new()
        {
            ["reconnect"] = 1,
            ["reconnect_streamed"] = 1,
            ["reconnect_on_network_error"] = 1
        };
        if (options is not null)
        {
            ApplyOptions(ffmpegOptions, options);
        }

        config.FFmpegOptions = ffmpegOptions;
        return config;
    }

    /// <summary>
    /// Translates the legacy command-line style options into FFmpeg AVFormat options.
    /// </summary>
    private static void ApplyOptions(PropertySet target, IEnumerable<string> options)
    {
        foreach (string option in options)
        {
            if (string.IsNullOrWhiteSpace(option))
            {
                continue;
            }

            string normalized = option.Trim().TrimStart('-');
            int separator = normalized.IndexOf('=');
            if (separator <= 0 || separator == normalized.Length - 1)
            {
                // Flag style options have no FFmpeg equivalent here; skip rather than
                // handing an invalid value to the native AVFormatContext.
                continue;
            }

            string key = normalized[..separator];
            string value = normalized[(separator + 1)..];
            target[key] = value;
        }
    }
}
