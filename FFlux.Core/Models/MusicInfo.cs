using System;
using Windows.Storage.FileProperties;

namespace Screenbox.Core.Models;

public sealed class MusicInfo : IMediaProperties
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string AlbumArtist { get; set; } = string.Empty;
    public string Composers { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public uint TrackNumber { get; set; }
    public uint Year { get; set; }
    public TimeSpan Duration { get; set; }
    public uint Bitrate { get; set; }

    /// <summary>Codec short name as reported by FFmpeg, for example <c>aac</c> or <c>flac</c>.</summary>
    public string Codec { get; set; } = string.Empty;

    /// <summary>Samples per second, for example 44100.</summary>
    public uint SampleRate { get; set; }

    /// <summary>Number of audio channels, for example 2.</summary>
    public uint Channels { get; set; }

    /// <summary>FFmpeg channel layout name, for example <c>stereo</c> or <c>5.1</c>.</summary>
    public string ChannelLayout { get; set; } = string.Empty;

    /// <summary>Bits per sample, for example 16.</summary>
    public uint BitsPerSample { get; set; }

    public MusicInfo() { }

    public MusicInfo(MusicProperties musicProperties)
    {
        Title = musicProperties.Title;
        Artist = musicProperties.Artist;
        Album = musicProperties.Album;
        AlbumArtist = musicProperties.AlbumArtist;
        Composers = string.Join(", ", musicProperties.Composers);
        Genre = string.Join(", ", musicProperties.Genre);
        TrackNumber = musicProperties.TrackNumber;
        Year = musicProperties.Year;
        Duration = musicProperties.Duration;
        Bitrate = musicProperties.Bitrate;
    }
}