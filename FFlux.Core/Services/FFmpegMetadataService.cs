using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFmpegInteropX;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Windows.Storage.Streams;

namespace Screenbox.Core.Services;

/// <summary>
/// Playback metadata service backed by FFmpegInteropX metadata tags and stream information.
/// </summary>
public sealed class FFmpegMetadataService : IPlaybackMetadataService
{
    private readonly ILogger<FFmpegMetadataService> _logger;

    /// <summary>
    /// Creates the metadata service.
    /// </summary>
    public FFmpegMetadataService(ILogger<FFmpegMetadataService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<MediaInfo?> GetMediaInfoAsync(
        IPlaybackItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item is not FFmpegPlaybackItem ffmpegItem)
        {
            throw new NotSupportedException("Only FFmpegPlaybackItem is supported.");
        }

        try
        {
            FFmpegMediaSource source = await ffmpegItem.OpenTask.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return BuildMediaInfo(source);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract media metadata from the FFmpeg source.");
            return null;
        }
    }

    /// <summary>
    /// Converts FFmpeg format tags and stream information into a <see cref="MediaInfo"/>.
    /// </summary>
    private static MediaInfo BuildMediaInfo(FFmpegMediaSource source)
    {
        // Use var for the WinRT projection: IMapView/IVectorView map to
        // IReadOnlyDictionary/IReadOnlyList in CsWinRT but not in every projection mode.
        var tags = source.MetadataTags;
        VideoStreamInfo? video = source.VideoStreams.FirstOrDefault();
        AudioStreamInfo? audio = source.AudioStreams.FirstOrDefault();

        string Tag(params string[] keys)
        {
            foreach (string key in keys)
            {
                if (tags.TryGetValue(key, out var values) && values.Count > 0)
                {
                    return values[0] ?? string.Empty;
                }
            }

            return string.Empty;
        }

        MediaPlaybackType mediaType = video is not null
            ? MediaPlaybackType.Video
            : audio is not null
                ? MediaPlaybackType.Music
                : MediaPlaybackType.Unknown;

        TimeSpan duration = source.Duration;
        string title = NormalizeTitle(Tag( "title"), source);
        string artist = Tag( "artist");
        string album = Tag( "album");
        string albumArtist = Tag( "album_artist", "albumartist");
        string genre = Tag( "genre");
        string description = Tag( "description", "comment");
        string showName = Tag( "show_name", "showname");
        string season = Tag( "season_number", "season");
        string episode = Tag( "episode_number", "episode", "episode_id");
        uint trackNumber = ParseTrackNumber(Tag( "track"));
        uint year = ParseYear(Tag( "date", "year"));
        string lyricsText = EmbeddedLyricsReader.FindLyricsText(EnumerateTags(tags)) ?? string.Empty;

        uint videoWidth = video is { PixelWidth: > 0 } ? (uint)video.PixelWidth : 0;
        uint videoHeight = video is { PixelHeight: > 0 } ? (uint)video.PixelHeight : 0;

        // Per-stream bitrate is frequently absent (MKV almost never carries it), so fall back to
        // the container-wide bitrate rather than showing 0 kbps for a file that plays fine.
        long? rawVideoBitrate = video?.Bitrate is > 0 ? video.Bitrate : source.FormatInfo?.Bitrate;
        uint videoBitrate = ToUInt32(rawVideoBitrate);
        uint audioBitrate = ToUInt32(audio?.Bitrate);

        MusicInfo musicInfo = new()
        {
            Title = title,
            Artist = artist,
            Album = album,
            AlbumArtist = albumArtist,
            Genre = genre,
            TrackNumber = trackNumber,
            Year = year,
            Duration = duration,
            Bitrate = audioBitrate,
            Codec = audio?.CodecName ?? string.Empty,
            SampleRate = audio is { SampleRate: > 0 } ? (uint)audio.SampleRate : 0,
            Channels = audio is { Channels: > 0 } ? (uint)audio.Channels : 0,
            ChannelLayout = audio?.ChannelLayout ?? string.Empty,
            BitsPerSample = audio is { BitsPerSample: > 0 } ? (uint)audio.BitsPerSample : 0
        };

        VideoInfo videoInfo = new()
        {
            Title = title,
            Subtitle = description,
            Year = year,
            Duration = duration,
            Width = videoWidth,
            Height = videoHeight,
            Bitrate = videoBitrate,
            Codec = video?.CodecName ?? string.Empty,
            FramesPerSecond = video is { FramesPerSecond: > 0 } ? video.FramesPerSecond : 0,
            BitsPerSample = video is { BitsPerSample: > 0 } ? (uint)video.BitsPerSample : 0,
            HasHdr = video?.HasHdrMetadata == true,
            ShowName = showName,
            Season = season,
            Episode = episode
        };

        // Cover art lives in the container as an attachment, so it is available as raw pixels
        // rather than as a path. Carrying the bytes lets the player page show album art for
        // formats where the shell thumbnail provider only offers a generic file-type icon.
        byte[]? artworkData = TryExtractArtwork(source);

        return new MediaInfo(mediaType, musicInfo, videoInfo)
        {
            ArtworkData = artworkData,
            LyricsText = lyricsText,
            Container = source.FormatInfo?.FormatName ?? string.Empty,
            AudioStreamCount = source.AudioStreams.Count,
            SubtitleStreamCount = source.SubtitleStreams.Count,
            ChapterCount = source.ChapterInfos.Count
        };
    }

    /// <summary>
    /// Flattens FFmpeg's tag map into key/value pairs for the AOT-safe lyrics reader.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> EnumerateTags(
        IReadOnlyDictionary<string, IReadOnlyList<string>> tags)
    {
        foreach (KeyValuePair<string, IReadOnlyList<string>> tag in tags)
        {
            if (tag.Value is null) continue;
            foreach (string value in tag.Value)
            {
                yield return new KeyValuePair<string, string>(tag.Key, value ?? string.Empty);
            }
        }
    }

    private static byte[]? TryExtractArtwork(FFmpegMediaSource source)
    {
        if (!source.HasThumbnail)
        {
            return null;
        }

        MediaThumbnailData? thumbnail = null;
        try
        {
            thumbnail = source.ExtractThumbnail();
            IBuffer? buffer = thumbnail?.Buffer;
            if (buffer is null || buffer.Length == 0)
            {
                return null;
            }

            byte[] bytes = new byte[buffer.Length];
            using DataReader reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception)
        {
            // A malformed cover must not fail metadata extraction; the UI falls back to the
            // generic placeholder.
            return null;
        }
        finally
        {
            thumbnail?.Dispose();
        }
    }

    private static string NormalizeTitle(string? rawTitle, FFmpegMediaSource source)
    {
        if (string.IsNullOrWhiteSpace(rawTitle) || Guid.TryParse(rawTitle, out _))
        {
            return string.Empty;
        }

        string format = source.FormatInfo?.FormatName ?? string.Empty;
        if (string.Equals(rawTitle, format, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return rawTitle;
    }

    private static uint ToUInt32(long? value) =>
        value is > 0 and <= uint.MaxValue ? (uint)value.Value : 0;

    private static uint ParseTrackNumber(string rawTrackNumber)
    {
        if (string.IsNullOrWhiteSpace(rawTrackNumber))
        {
            return 0;
        }

        ReadOnlySpan<char> span = rawTrackNumber.AsSpan().Trim();
        int slashIndex = span.IndexOf('/');
        if (slashIndex >= 0)
        {
            span = span[..slashIndex].Trim();
        }

        return uint.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint trackNumber)
            ? trackNumber
            : 0;
    }

    private static uint ParseYear(string rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate))
        {
            return 0;
        }

        ReadOnlySpan<char> span = rawDate.AsSpan().Trim();
        if (uint.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint year))
        {
            return year;
        }

        if (span.Length >= 4 &&
            uint.TryParse(span[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint prefixYear))
        {
            return prefixYear;
        }

        return DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset dto)
            ? (uint)dto.Year
            : 0;
    }
}
