using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFmpegInteropX;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Enums;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;

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
        string title = NormalizeTitle(Tag(tags, "title"), source);
        string artist = Tag(tags, "artist");
        string album = Tag(tags, "album");
        string albumArtist = Tag(tags, "album_artist", "albumartist");
        string genre = Tag(tags, "genre");
        string description = Tag(tags, "description", "comment");
        string showName = Tag(tags, "show_name", "showname");
        string season = Tag(tags, "season_number", "season");
        string episode = Tag(tags, "episode_number", "episode", "episode_id");
        uint trackNumber = ParseTrackNumber(Tag(tags, "track"));
        uint year = ParseYear(Tag(tags, "date", "year"));

        uint videoWidth = video is { PixelWidth: > 0 } ? (uint)video.PixelWidth : 0;
        uint videoHeight = video is { PixelHeight: > 0 } ? (uint)video.PixelHeight : 0;
        uint videoBitrate = ToUInt32(video?.Bitrate);
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
            Bitrate = audioBitrate
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
            ShowName = showName,
            Season = season,
            Episode = episode
        };

        // Embedded cover art is exposed as raw bytes by MediaThumbnailData, not as a URI.
        // Screenbox's artwork pipeline expects a URI, so it is intentionally left empty.
        return new MediaInfo(mediaType, musicInfo, videoInfo);
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
