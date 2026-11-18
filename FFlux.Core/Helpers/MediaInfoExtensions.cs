using System;
using Screenbox.Core.Enums;
using Screenbox.Core.Models;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Provides extension methods for <see cref="MediaInfo"/> instances.
/// </summary>
public static class MediaInfoExtensions
{
    /// <summary>
    /// Returns a new <see cref="MediaInfo"/> instance where empty or default-value fields in <paramref name="target"/>
    /// are updated by non-empty / non-default values from <paramref name="source"/>, preserving any fields that were
    /// already populated in <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The base media info instance whose populated fields take precedence.</param>
    /// <param name="source">The incoming media info instance providing fallback values for empty/default fields.</param>
    /// <returns>A new <see cref="MediaInfo"/> containing the selectively merged properties.</returns>
    public static MediaInfo MergeSelective(this MediaInfo target, MediaInfo source)
    {
        MediaPlaybackType mediaType = target.MediaType != MediaPlaybackType.Unknown
            ? target.MediaType
            : source.MediaType;

        // "First non-empty wins" for every field. Local helpers keep the mapping below readable
        // instead of repeating the same ternary for dozens of properties.
        static string Text(string preferred, string fallback) =>
            !string.IsNullOrEmpty(preferred) ? preferred : fallback;

        static uint Number(uint preferred, uint fallback) => preferred > 0 ? preferred : fallback;

        static int Count(int preferred, int fallback) => preferred > 0 ? preferred : fallback;

        static double Ratio(double preferred, double fallback) => preferred > 0 ? preferred : fallback;

        static TimeSpan Span(TimeSpan preferred, TimeSpan fallback) =>
            preferred > TimeSpan.Zero ? preferred : fallback;

        ulong size = target.Size > 0 ? target.Size : source.Size;
        DateTimeOffset dateModified = target.DateModified != default ? target.DateModified : source.DateModified;
        Uri? artworkUri = target.ArtworkUri ?? source.ArtworkUri;
        byte[]? artworkData = target.ArtworkData ?? source.ArtworkData;
        string lyricsText = Text(target.LyricsText, source.LyricsText);

        MusicInfo targetMusic = target.MusicProperties;
        MusicInfo sourceMusic = source.MusicProperties;
        MusicInfo musicInfo = new()
        {
            Title = Text(targetMusic.Title, sourceMusic.Title),
            Artist = Text(targetMusic.Artist, sourceMusic.Artist),
            Album = Text(targetMusic.Album, sourceMusic.Album),
            AlbumArtist = Text(targetMusic.AlbumArtist, sourceMusic.AlbumArtist),
            Composers = Text(targetMusic.Composers, sourceMusic.Composers),
            Genre = Text(targetMusic.Genre, sourceMusic.Genre),
            TrackNumber = Number(targetMusic.TrackNumber, sourceMusic.TrackNumber),
            Year = Number(targetMusic.Year, sourceMusic.Year),
            Duration = Span(targetMusic.Duration, sourceMusic.Duration),
            Bitrate = Number(targetMusic.Bitrate, sourceMusic.Bitrate),
            Codec = Text(targetMusic.Codec, sourceMusic.Codec),
            SampleRate = Number(targetMusic.SampleRate, sourceMusic.SampleRate),
            Channels = Number(targetMusic.Channels, sourceMusic.Channels),
            ChannelLayout = Text(targetMusic.ChannelLayout, sourceMusic.ChannelLayout),
            BitsPerSample = Number(targetMusic.BitsPerSample, sourceMusic.BitsPerSample)
        };

        VideoInfo targetVideo = target.VideoProperties;
        VideoInfo sourceVideo = source.VideoProperties;
        VideoInfo videoInfo = new()
        {
            Title = Text(targetVideo.Title, sourceVideo.Title),
            Subtitle = Text(targetVideo.Subtitle, sourceVideo.Subtitle),
            Producers = Text(targetVideo.Producers, sourceVideo.Producers),
            Writers = Text(targetVideo.Writers, sourceVideo.Writers),
            Duration = Span(targetVideo.Duration, sourceVideo.Duration),
            Year = Number(targetVideo.Year, sourceVideo.Year),
            Width = Number(targetVideo.Width, sourceVideo.Width),
            Height = Number(targetVideo.Height, sourceVideo.Height),
            Bitrate = Number(targetVideo.Bitrate, sourceVideo.Bitrate),
            Codec = Text(targetVideo.Codec, sourceVideo.Codec),
            FramesPerSecond = Ratio(targetVideo.FramesPerSecond, sourceVideo.FramesPerSecond),
            BitsPerSample = Number(targetVideo.BitsPerSample, sourceVideo.BitsPerSample),
            HasHdr = targetVideo.HasHdr || sourceVideo.HasHdr,
            ShowName = Text(targetVideo.ShowName, sourceVideo.ShowName),
            Season = Text(targetVideo.Season, sourceVideo.Season),
            Episode = Text(targetVideo.Episode, sourceVideo.Episode)
        };

        return new MediaInfo(mediaType, musicInfo, videoInfo)
        {
            Size = size,
            DateModified = dateModified,
            ArtworkUri = artworkUri,
            ArtworkData = artworkData,
            LyricsText = lyricsText,
            Container = Text(target.Container, source.Container),
            AudioStreamCount = Count(target.AudioStreamCount, source.AudioStreamCount),
            SubtitleStreamCount = Count(target.SubtitleStreamCount, source.SubtitleStreamCount),
            ChapterCount = Count(target.ChapterCount, source.ChapterCount)
        };
    }
}
