using System;
using Screenbox.Core.Enums;
using Windows.Storage.FileProperties;

namespace Screenbox.Core.Models;

public sealed class MediaInfo
{
    public MediaPlaybackType MediaType { get; set; }

    public VideoInfo VideoProperties { get; }

    public MusicInfo MusicProperties { get; }

    public ulong Size { get; set; }

    public DateTimeOffset DateModified { get; set; }

    public Uri? ArtworkUri { get; set; }

    /// <summary>
    /// Gets or sets the encoded (PNG/JPEG) cover art embedded in the media file itself.
    /// </summary>
    /// <remarks>
    /// Containers such as MP3, FLAC, M4A and MKV carry their cover as an attachment rather than
    /// as a separate file, and the shell thumbnail provider does not always surface it. FFmpeg
    /// reads it reliably, so the bytes are carried here and decoded on demand.
    /// </remarks>
    public byte[]? ArtworkData { get; set; }

    public MediaInfo(MediaPlaybackType mediaType, string title = "", uint year = default, TimeSpan duration = default)
    {
        MediaType = mediaType;
        VideoProperties = new VideoInfo();
        MusicProperties = new MusicInfo();

        VideoProperties.Title = title;
        VideoProperties.Duration = duration;
        VideoProperties.Year = year;
        MusicProperties.Title = title;
        MusicProperties.Duration = duration;
        MusicProperties.Year = year;
    }

    internal MediaInfo(IMediaProperties properties)
    {
        MediaType = MediaPlaybackType.Unknown;
        if (properties is MusicInfo musicProperties)
        {
            MediaType = MediaPlaybackType.Music;
            MusicProperties = musicProperties;
            VideoProperties = new VideoInfo();
        }
        else if (properties is VideoInfo videoProperties)
        {
            MediaType = MediaPlaybackType.Video;
            MusicProperties = new MusicInfo();
            VideoProperties = videoProperties;
        }
        else
        {
            throw new ArgumentException("Invalid media properties type.");
        }
    }

    public MediaInfo(BasicProperties basicProperties, MusicProperties musicProperties)
    {
        Size = basicProperties.Size;
        DateModified = basicProperties.DateModified;
        MediaType = MediaPlaybackType.Music;
        MusicProperties = new MusicInfo(musicProperties);
        VideoProperties = new VideoInfo();
    }

    public MediaInfo(BasicProperties basicProperties, VideoProperties videoProperties)
    {
        Size = basicProperties.Size;
        DateModified = basicProperties.DateModified;
        MediaType = MediaPlaybackType.Video;
        MusicProperties = new MusicInfo();
        VideoProperties = new VideoInfo(videoProperties);
    }

    public MediaInfo(MediaPlaybackType mediaType, MusicInfo musicProperties, VideoInfo videoProperties)
    {
        MediaType = mediaType;
        MusicProperties = musicProperties;
        VideoProperties = videoProperties;
    }
}
