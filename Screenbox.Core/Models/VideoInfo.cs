using System;
using Windows.Storage.FileProperties;

namespace Screenbox.Core.Models;

public sealed class VideoInfo : IMediaProperties
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Producers { get; set; } = string.Empty;
    public string Writers { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public uint Year { get; set; }
    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint Bitrate { get; set; }

    /// <summary>Codec short name as reported by FFmpeg, for example <c>h264</c> or <c>hevc</c>.</summary>
    public string Codec { get; set; } = string.Empty;

    /// <summary>Frames per second, for example 23.976.</summary>
    public double FramesPerSecond { get; set; }

    /// <summary>Bits per sample of the decoded picture, for example 8 or 10.</summary>
    public uint BitsPerSample { get; set; }

    /// <summary>Whether the stream carries HDR mastering metadata.</summary>
    public bool HasHdr { get; set; }

    /** Container metadata **/
    public string ShowName { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string Episode { get; set; } = string.Empty;

    public VideoInfo() { }

    public VideoInfo(VideoProperties videoProperties)
    {
        Title = videoProperties.Title;
        Subtitle = videoProperties.Subtitle;
        Year = videoProperties.Year;
        Producers = string.Join(", ", videoProperties.Producers);
        Writers = string.Join(", ", videoProperties.Writers);
        Duration = videoProperties.Duration;
        Width = videoProperties.Width;
        Height = videoProperties.Height;
        Bitrate = videoProperties.Bitrate;
    }
}