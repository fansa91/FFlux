using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFmpegInteropX;
using Screenbox.Core.Helpers;
using Screenbox.Core.MediaSources;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using IMediaSource = Screenbox.Core.MediaSources.IMediaSource;

namespace Screenbox.Core.Playback;

/// <summary>
/// A playback item backed by an <see cref="FFmpegMediaSource"/> and its
/// <see cref="MediaPlaybackItem"/>.
/// </summary>
/// <remarks>
/// FFmpegInteropX opens media asynchronously (<c>CreateFromStreamAsync</c> /
/// <c>CreateFromUriAsync</c>) while <see cref="IPlayerService"/> exposes a synchronous
/// factory. The item therefore starts opening in its constructor and exposes a task that
/// <see cref="FFmpegMediaPlayer"/> awaits before assigning the source and calling Play().
/// </remarks>
public sealed partial class FFmpegPlaybackItem : IPlaybackItem
{
    /// <inheritdoc/>
    public IMediaSource Source { get; }

    /// <inheritdoc/>
    public TimeSpan? Duration => FFmpegSource is { } source && source.Duration > TimeSpan.Zero
        ? source.Duration
        : null;

    public FFmpegAudioTrackList AudioTracks { get; } = new();

    public FFmpegVideoTrackList VideoTracks { get; } = new();

    public FFmpegSubtitleTrackList SubtitleTracks { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<ChapterCue> Chapters { get; private set; } = [];

    ISingleSelectMediaTrackList<AudioTrack> IPlaybackItem.AudioTracks => AudioTracks;

    ISingleSelectMediaTrackList<VideoTrack> IPlaybackItem.VideoTracks => VideoTracks;

    ISubtitleTrackList IPlaybackItem.SubtitleTracks => SubtitleTracks;

    /// <summary>
    /// Gets the FFmpeg source once opening has completed; otherwise <see langword="null"/>.
    /// </summary>
    internal FFmpegMediaSource? FFmpegSource { get; private set; }

    /// <summary>
    /// Gets the playback item once opening has completed; otherwise <see langword="null"/>.
    /// </summary>
    internal MediaPlaybackItem? MediaPlaybackItem { get; private set; }

    /// <summary>
    /// Completes when the FFmpeg source has been created and its tracks and chapters are ready.
    /// </summary>
    internal Task<FFmpegMediaSource> OpenTask { get; }

    private bool _isDisposed;
    private IRandomAccessStream? _ownedStream;

    /// <summary>
    /// Creates a playback item and starts opening the underlying FFmpeg source.
    /// </summary>
    /// <param name="source">The media source to open.</param>
    /// <param name="options">Optional FFmpeg options supplied by the user.</param>
    public FFmpegPlaybackItem(IMediaSource source, IEnumerable<string>? options = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        OpenTask = OpenAsync(options);
    }

    private async Task<FFmpegMediaSource> OpenAsync(IEnumerable<string>? options)
    {
        PlaybackTrace.Write($"OpenAsync start: {Source.GetType().Name} / {Source}");
        try
        {
            MediaSourceConfig config = FFmpegMediaSourceConfigFactory.Create(options);
            FFmpegMediaSource ffmpegSource = await CreateSourceAsync(config);

            FFmpegSource = ffmpegSource;
            MediaPlaybackItem = ffmpegSource.CreateMediaPlaybackItem();
            PlaybackTrace.Write(
                $"FFmpegMediaSource created: duration={ffmpegSource.Duration}, playbackItem={MediaPlaybackItem is not null}");

            AudioTracks.Attach(ffmpegSource, MediaPlaybackItem);
            VideoTracks.Attach(ffmpegSource, MediaPlaybackItem);
            SubtitleTracks.Attach(ffmpegSource, MediaPlaybackItem);
            PlaybackTrace.Write(
                $"Tracks attached: audio={AudioTracks.Count}, video={VideoTracks.Count}, subtitle={SubtitleTracks.Count}");

            List<Screenbox.Core.Models.ChapterInfo> rawChapters = ffmpegSource.ChapterInfos
                .Select(c => new Screenbox.Core.Models.ChapterInfo(c.StartTime, c.Duration, c.Title))
                .ToList();
            Chapters = ChapterHelper.Sanitize(rawChapters, ffmpegSource.Duration);

            return ffmpegSource;
        }
        catch (Exception ex)
        {
            PlaybackTrace.Write("OpenAsync FAILED", ex);
            throw;
        }
    }

    private async Task<FFmpegMediaSource> CreateSourceAsync(MediaSourceConfig config)
    {
        switch (Source)
        {
            case IFileMediaSource fileSource:
                StorageFile? file = fileSource.StorageFile ?? await fileSource.GetStorageFileAsync();
                if (file is not null)
                {
                    // FFmpegInteropX reads (and seeks) through this stream for the whole
                    // playback session, so it must not be disposed until the item is disposed.
                    _ownedStream = await file.OpenAsync(FileAccessMode.Read);
                    return await FFmpegMediaSource.CreateFromStreamAsync(_ownedStream, config);
                }

                return await FFmpegMediaSource.CreateFromUriAsync(
                    new Uri(fileSource.Path).AbsoluteUri, config);

            case IUriMediaSource uriSource:
                return await FFmpegMediaSource.CreateFromUriAsync(uriSource.Uri.AbsoluteUri, config);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(Source), $"Unsupported media source type: {Source.GetType().Name}");
        }
    }

    /// <inheritdoc/>
    public async Task WaitForParsedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        Task completed = await Task.WhenAny(OpenTask, Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token));
        if (completed != OpenTask)
        {
            throw new OperationCanceledException(timeoutCts.Token);
        }

        await OpenTask;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExpandedMediaItem>> ExpandSubItemsAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        // FFmpegInteropX demuxes a single container and does not enumerate playlist
        // sub-items. Playlist manifests are expanded by their own media source instead.
        if (Source is not IExpandableMediaSource expandable)
        {
            return Array.Empty<ExpandedMediaItem>();
        }

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        return await expandable.ExpandAsync(timeoutCts.Token);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        // Opening may still be in flight; dispose once it finishes so the native decoder
        // is never released mid-initialization.
        _ = DisposeWhenOpenedAsync();
        GC.SuppressFinalize(this);
    }

    private async Task DisposeWhenOpenedAsync()
    {
        try
        {
            FFmpegMediaSource source = await OpenTask;
            source.Dispose();
        }
        catch (Exception)
        {
            // Opening failed: there is nothing native to release.
        }
        finally
        {
            AudioTracks.Detach();
            VideoTracks.Detach();
            SubtitleTracks.Detach();
            _ownedStream?.Dispose();
            _ownedStream = null;
            FFmpegSource = null;
            MediaPlaybackItem = null;
        }
    }
}
