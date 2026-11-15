namespace Screenbox.Core.Enums;

public enum MediaProperty
{
    Title,
    Subtitle,
    Year,
    Producers,
    Writers,
    Length,
    Resolution,
    BitRate,
    ContributingArtists,
    Album,
    AlbumArtist,
    Composers,
    Genre,
    Track,
    FileType,
    ContentType,
    Size,
    LastModified,

    /// <summary>Video codec short name, for example "h264".</summary>
    VideoCodec,

    /// <summary>Audio codec short name, for example "aac".</summary>
    AudioCodec,

    /// <summary>Video frame rate in frames per second.</summary>
    FrameRate,

    /// <summary>Bits per sample of the decoded audio or video stream.</summary>
    BitDepth,

    /// <summary>Dynamic range marker, shown only when the video carries HDR metadata.</summary>
    DynamicRange,

    /// <summary>Audio sample rate.</summary>
    SampleRate,

    /// <summary>Audio channel count and layout.</summary>
    Channels,

    /// <summary>Container (demuxer) name, for example "matroska,webm".</summary>
    Container,

    /// <summary>Number of audio streams in the container.</summary>
    AudioTrackCount,

    /// <summary>Number of subtitle streams in the container.</summary>
    SubtitleTrackCount,

    /// <summary>Number of chapters declared by the container.</summary>
    ChapterCount,
}
