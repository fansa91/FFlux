using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.MediaSources;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using Windows.Storage;

namespace Screenbox.Core.ViewModels;

public sealed partial class PropertyViewModel : ObservableObject
{
    public IReadOnlyList<MediaMetadata> MediaProperties { get; private set; } = Array.Empty<MediaMetadata>();

    public IReadOnlyList<MediaMetadata> VideoProperties { get; private set; } = Array.Empty<MediaMetadata>();

    public IReadOnlyList<MediaMetadata> AudioProperties { get; private set; } = Array.Empty<MediaMetadata>();

    public IReadOnlyList<MediaMetadata> TrackProperties { get; private set; } = Array.Empty<MediaMetadata>();

    public IReadOnlyList<MediaMetadata> FileProperties { get; private set; } = Array.Empty<MediaMetadata>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFileLocationCommand))]
    public partial bool CanNavigateToFile { get; set; }

    private readonly IFilesService _filesService;
    private readonly ISourceMetadataService _sourceMetadataService;
    private readonly IPlaybackMetadataService _playbackMetadataService;
    private readonly PlayerContext _playerContext;
    private readonly IPlayerService _playerService;
    private readonly IPlayQueueBufferManager _bufferManager;
    private StorageFile? _mediaFile;
    private Uri? _mediaUri;
    private string? _mediaFilePath;

    /// <summary>
    /// FFmpeg reports codecs by their short internal names. The panel shows the names people
    /// recognise from release notes and players instead; anything unmapped is shown verbatim.
    /// </summary>
    private static readonly Dictionary<string, string> FriendlyCodecNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["h264"] = "H.264 / AVC",
        ["hevc"] = "H.265 / HEVC",
        ["av1"] = "AV1",
        ["vp8"] = "VP8",
        ["vp9"] = "VP9",
        ["mpeg4"] = "MPEG-4",
        ["mpeg2video"] = "MPEG-2",
        ["aac"] = "AAC",
        ["mp3"] = "MP3",
        ["mp2"] = "MP2",
        ["flac"] = "FLAC",
        ["alac"] = "ALAC",
        ["opus"] = "Opus",
        ["vorbis"] = "Vorbis",
        ["ac3"] = "AC-3",
        ["eac3"] = "E-AC-3",
        ["truehd"] = "TrueHD",
        ["dts"] = "DTS",
        ["wmav2"] = "WMA",
        ["pcm_s16le"] = "PCM",
        ["pcm_s24le"] = "PCM",
        ["pcm_s32le"] = "PCM",
        ["pcm_f32le"] = "PCM",
    };

    /// <summary>
    /// Audio codecs whose sample format is well defined. Lossy decoders report the width of
    /// their internal float buffer (AAC answers 32-bit for a 16-bit source), so the bit depth is
    /// only displayed for codecs where the number describes the actual content.
    /// </summary>
    private static readonly HashSet<string> LosslessCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "flac",
        "alac",
        "ape",
        "tta",
        "wavpack",
        "truehd",
    };

    public PropertyViewModel(
        IFilesService filesService,
        ISourceMetadataService sourceMetadataService,
        IPlaybackMetadataService playbackMetadataService,
        PlayerContext playerContext,
        IPlayerService playerService,
        IPlayQueueBufferManager bufferManager)
    {
        _filesService = filesService;
        _sourceMetadataService = sourceMetadataService;
        _playbackMetadataService = playbackMetadataService;
        _playerContext = playerContext;
        _playerService = playerService;
        _bufferManager = bufferManager;
    }

    /// <summary>
    /// Loads media metadata on demand for the specified <paramref name="media"/> item
    /// and refreshes the property collections.
    /// </summary>
    /// <param name="media">The media view model whose properties are being inspected.</param>
    public async Task LoadPropertiesAsync(MediaViewModel media)
    {
        if (!media.DetailsLoaded)
        {
            await media.LoadDetailsAsync(_sourceMetadataService);
        }

        if (_bufferManager.TryGetPlaybackItem(media, out IPlaybackItem? bufferedItem))
        {
            await media.LoadDetailsAsync(_playbackMetadataService, bufferedItem);
        }
        else if (_playerContext.MediaPlayer is { } mediaPlayer)
        {
            if (mediaPlayer.PlaybackItem is { } activeItem && ReferenceEquals(activeItem.Source, media.Source))
            {
                await media.LoadDetailsAsync(_playbackMetadataService, activeItem);
            }
            else
            {
                try
                {
                    using IPlaybackItem tempItem = _playerService.CreatePlaybackItem(
                        mediaPlayer,
                        media.Source,
                        media.Options.Count > 0 ? media.Options.ToArray() : Array.Empty<string>());
                    await media.LoadDetailsAsync(_playbackMetadataService, tempItem);
                }
                catch (Exception)
                {
                    // Ignore playback item creation failures during property inspection
                }
            }
        }

        UpdateProperties(media);
    }

    /// <summary>
    /// Populates the property collections with metadata keys and values from the given <paramref name="media"/> item.
    /// </summary>
    /// <remarks>
    /// Rows are only emitted when there is something to show. Most files have no writers, no
    /// composers and no HDR metadata, and a wall of blank rows hides the fields that do have
    /// values.
    /// </remarks>
    public void UpdateProperties(MediaViewModel media)
    {
        List<MediaMetadata> mediaEntries = new();
        List<MediaMetadata> videoEntries = new();
        List<MediaMetadata> audioEntries = new();
        List<MediaMetadata> trackEntries = new();
        List<MediaMetadata> fileEntries = new();

        MusicInfo music = media.MediaInfo.MusicProperties;
        VideoInfo video = media.MediaInfo.VideoProperties;

        switch (media.MediaType)
        {
            case MediaPlaybackType.Video:
                Add(mediaEntries, MediaProperty.Title, FirstNonEmpty(video.Title, media.Name));
                Add(mediaEntries, MediaProperty.Subtitle, video.Subtitle);
                Add(mediaEntries, MediaProperty.Year, FormatYear(video.Year));
                Add(mediaEntries, MediaProperty.Producers, video.Producers);
                Add(mediaEntries, MediaProperty.Writers, video.Writers);
                Add(mediaEntries, MediaProperty.Length, FormatDuration(video.Duration));

                Add(videoEntries, MediaProperty.Resolution, FormatResolution(video.Width, video.Height));
                Add(videoEntries, MediaProperty.FrameRate, FormatFrameRate(video.FramesPerSecond));
                Add(videoEntries, MediaProperty.VideoCodec, FormatCodec(video.Codec));
                Add(videoEntries, MediaProperty.BitDepth, FormatBitDepth(video.BitsPerSample));
                Add(videoEntries, MediaProperty.DynamicRange, video.HasHdr ? "HDR" : null);
                Add(videoEntries, MediaProperty.BitRate, FormatBitRate(video.Bitrate));

                Add(audioEntries, MediaProperty.AudioCodec, FormatCodec(music.Codec));
                Add(audioEntries, MediaProperty.Channels, FormatChannels(music.Channels, music.ChannelLayout));
                Add(audioEntries, MediaProperty.SampleRate, FormatSampleRate(music.SampleRate));
                Add(audioEntries, MediaProperty.BitDepth, FormatAudioBitDepth(music.Codec, music.BitsPerSample));
                Add(audioEntries, MediaProperty.BitRate, FormatBitRate(music.Bitrate));

                // Stream counts only mean something when there is more than the obvious one, so
                // they are added per count rather than as a fixed block.
                Add(trackEntries, MediaProperty.AudioTrackCount, FormatCount(media.MediaInfo.AudioStreamCount));
                Add(trackEntries, MediaProperty.SubtitleTrackCount, FormatCount(media.MediaInfo.SubtitleStreamCount));
                Add(trackEntries, MediaProperty.ChapterCount, FormatCount(media.MediaInfo.ChapterCount));
                break;

            case MediaPlaybackType.Music:
                Add(mediaEntries, MediaProperty.Title, FirstNonEmpty(music.Title, media.Name));
                Add(mediaEntries, MediaProperty.ContributingArtists, music.Artist);
                Add(mediaEntries, MediaProperty.Album, music.Album);
                Add(mediaEntries, MediaProperty.AlbumArtist, music.AlbumArtist);
                Add(mediaEntries, MediaProperty.Composers, music.Composers);
                Add(mediaEntries, MediaProperty.Genre, music.Genre);
                Add(mediaEntries, MediaProperty.Track, FormatCount(music.TrackNumber));
                Add(mediaEntries, MediaProperty.Year, FormatYear(music.Year));
                Add(mediaEntries, MediaProperty.Length, FormatDuration(music.Duration));

                Add(audioEntries, MediaProperty.AudioCodec, FormatCodec(music.Codec));
                Add(audioEntries, MediaProperty.Channels, FormatChannels(music.Channels, music.ChannelLayout));
                Add(audioEntries, MediaProperty.SampleRate, FormatSampleRate(music.SampleRate));
                Add(audioEntries, MediaProperty.BitDepth, FormatAudioBitDepth(music.Codec, music.BitsPerSample));
                Add(audioEntries, MediaProperty.BitRate, FormatBitRate(music.Bitrate));
                break;
        }

        switch (media.Source)
        {
            case IFileMediaSource fileSource:
                _mediaFile = fileSource.StorageFile;
                _mediaFilePath = fileSource.Path;
                _mediaUri = null;
                CanNavigateToFile = true;
                Add(fileEntries, MediaProperty.Container, media.MediaInfo.Container);
                Add(fileEntries, MediaProperty.FileType, fileSource.FileType);
                if (_mediaFile != null)
                {
                    Add(fileEntries, MediaProperty.ContentType, _mediaFile.ContentType);
                }

                Add(fileEntries, MediaProperty.Size, media.MediaInfo.Size > 0
                    ? BytesToHumanReadable((long)media.MediaInfo.Size)
                    : null);
                Add(fileEntries, MediaProperty.LastModified, media.MediaInfo.DateModified != default
                    ? media.MediaInfo.DateModified.ToString()
                    : null);
                break;
            case IUriMediaSource uriSource:
                _mediaFile = null;
                _mediaFilePath = null;
                _mediaUri = uriSource.Uri;
                CanNavigateToFile = uriSource.Uri.IsFile;
                break;
        }

        MediaProperties = mediaEntries;
        VideoProperties = videoEntries;
        AudioProperties = audioEntries;
        TrackProperties = trackEntries;
        FileProperties = fileEntries;
    }

    private static void Add(List<MediaMetadata> entries, MediaProperty key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            entries.Add(new MediaMetadata(key, value.Trim()));
        }
    }

    private static string? FirstNonEmpty(string? value, string fallback) =>
        !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string? FormatYear(uint year) => year > 0 ? year.ToString() : null;

    private static string? FormatCount(int count) => count > 0 ? count.ToString() : null;

    private static string? FormatCount(uint count) => count > 0 ? count.ToString() : null;

    private static string? FormatDuration(TimeSpan duration) =>
        duration > TimeSpan.Zero ? Humanizer.ToDuration(duration) : null;

    private static string? FormatResolution(uint width, uint height) =>
        width > 0 && height > 0 ? $"{width}×{height}" : null;

    private static string? FormatFrameRate(double framesPerSecond) =>
        framesPerSecond > 0
            ? framesPerSecond.ToString("0.###", CultureInfo.InvariantCulture) + " fps"
            : null;

    private static string? FormatSampleRate(uint sampleRate) =>
        sampleRate > 0
            ? (sampleRate / 1000d).ToString("0.###", CultureInfo.InvariantCulture) + " kHz"
            : null;

    private static string? FormatChannels(uint channels, string layout)
    {
        if (channels == 0)
        {
            return null;
        }

        // FFmpeg falls back to a plain "2 channels" description when it has no layout name for
        // the stream, which would render as the redundant "2 (2 channels)".
        if (string.IsNullOrWhiteSpace(layout) || IsRedundantChannelLayout(layout, channels))
        {
            return channels.ToString();
        }

        return $"{channels} ({layout})";
    }

    private static bool IsRedundantChannelLayout(string layout, uint channels) =>
        layout.StartsWith(channels.ToString(), StringComparison.Ordinal)
        && layout.Contains("channel", StringComparison.OrdinalIgnoreCase);

    private static string? FormatBitDepth(uint bitsPerSample) =>
        bitsPerSample > 0 ? $"{bitsPerSample}-bit" : null;

    private static string? FormatAudioBitDepth(string codec, uint bitsPerSample)
    {
        bool isLossless = LosslessCodecs.Contains(codec)
                          || codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase);

        return isLossless ? FormatBitDepth(bitsPerSample) : null;
    }

    private static string? FormatBitRate(uint bitsPerSecond)
    {
        if (bitsPerSecond == 0)
        {
            return null;
        }

        return bitsPerSecond >= 1_000_000
            ? (bitsPerSecond / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + " Mbps"
            : ((bitsPerSecond + 500) / 1000).ToString(CultureInfo.InvariantCulture) + " kbps";
    }

    private static string? FormatCodec(string codec)
    {
        if (string.IsNullOrWhiteSpace(codec))
        {
            return null;
        }

        return FriendlyCodecNames.TryGetValue(codec, out string? friendly) ? friendly : codec;
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToFile))]
    private void OpenFileLocation()
    {
        if (_mediaFile != null)
            _filesService.OpenFileLocationAsync(_mediaFile);
        else if (!string.IsNullOrEmpty(_mediaFilePath))
            _filesService.OpenFileLocationAsync(_mediaFilePath);
        else if (_mediaUri != null)
            _filesService.OpenFileLocationAsync(_mediaUri.OriginalString);
    }

    // https://stackoverflow.com/a/11124118
    private static string BytesToHumanReadable(long byteCount)
    {
        // Get absolute value
        long absCount = byteCount < 0 ? -byteCount : byteCount;
        // Determine the suffix and readable value
        string suffix;
        double readable;
        if (absCount >= 0x1000000000000000) // Exabyte
        {
            suffix = "EB";
            readable = byteCount >> 50;
        }
        else if (absCount >= 0x4000000000000) // Petabyte
        {
            suffix = "PB";
            readable = byteCount >> 40;
        }
        else if (absCount >= 0x10000000000) // Terabyte
        {
            suffix = "TB";
            readable = byteCount >> 30;
        }
        else if (absCount >= 0x40000000) // Gigabyte
        {
            suffix = "GB";
            readable = byteCount >> 20;
        }
        else if (absCount >= 0x100000) // Megabyte
        {
            suffix = "MB";
            readable = byteCount >> 10;
        }
        else if (absCount >= 0x400) // Kilobyte
        {
            suffix = "KB";
            readable = byteCount;
        }
        else
        {
            return byteCount.ToString("0 B"); // Byte
        }
        // Divide by 1024 to get fractional value
        readable = readable / 1024;
        // Return formatted number with suffix
        return readable.ToString("0.## ") + suffix;
    }
}
