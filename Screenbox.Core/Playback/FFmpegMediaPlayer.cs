using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFmpegInteropX;
using Screenbox.Core.Events;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using NativeMediaPlayer = Windows.Media.Playback.MediaPlayer;

namespace Screenbox.Core.Playback;

/// <summary>
/// <see cref="IMediaPlayer"/> implementation backed by a native
/// <see cref="Windows.Media.Playback.MediaPlayer"/> and an FFmpegInteropX
/// <see cref="FFmpegMediaSource"/>.
/// </summary>
/// <remarks>
/// The player is a long lived singleton: unlike the LibVLC swap chain there is no native
/// video surface to recreate, so it can be registered in DI once and attached to a
/// MediaPlayerElement with <c>SetMediaPlayer</c>.
/// </remarks>
public sealed class FFmpegMediaPlayer : IMediaPlayer
{
    public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaEnded;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaFailed;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaOpened;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? IsMutedChanged;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? VolumeChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<IPlaybackItem?>>? PlaybackItemChanged;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? BufferingProgressChanged;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? BufferingStarted;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? BufferingEnded;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<TimeSpan>>? NaturalDurationChanged;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? NaturalVideoSizeChanged;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? CanSeekChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<TimeSpan>>? PositionChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<ChapterCue?>>? ChapterChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<MediaPlaybackState>>? PlaybackStateChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<double>>? PlaybackRateChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<double>>? AudioDelayChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<double>>? SubtitleDelayChanged;

    /// <summary>
    /// Gets the native player that must be attached to a MediaPlayerElement.
    /// </summary>
    public NativeMediaPlayer NativePlayer => _player;

    /// <inheritdoc/>
    public bool CanPause => Session?.CanPause ?? false;

    /// <inheritdoc/>
    public bool CanSeek { get; private set; }

    /// <inheritdoc/>
    public bool IsMuted
    {
        get => _player.IsMuted;
        set => _player.IsMuted = value;
    }

    /// <inheritdoc/>
    public bool IsLoopingEnabled
    {
        get => _player.IsLoopingEnabled;
        set => _player.IsLoopingEnabled = value;
    }

    /// <inheritdoc/>
    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0, 1);
    }

    /// <inheritdoc/>
    public DeviceInformation? AudioDevice
    {
        get => _player.AudioDevice;
        set
        {
            // null means "keep the system default"; MediaPlayer rejects a null device.
            if (value is not null)
            {
                _player.AudioDevice = value;
            }
        }
    }

    /// <inheritdoc/>
    public MediaPlaybackState PlaybackState => Session?.PlaybackState ?? MediaPlaybackState.None;

    /// <inheritdoc/>
    public double BufferingProgress => Session?.BufferingProgress ?? 0;

    /// <inheritdoc/>
    public uint NaturalVideoHeight => Session?.NaturalVideoHeight ?? 0;

    /// <inheritdoc/>
    public uint NaturalVideoWidth => Session?.NaturalVideoWidth ?? 0;

    /// <inheritdoc/>
    public TimeSpan Position
    {
        get => Session?.Position ?? TimeSpan.Zero;
        set
        {
            MediaPlaybackSession? session = Session;
            if (session is null) return;

            TimeSpan oldValue = session.Position;
            TimeSpan clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            if (session.NaturalDuration > TimeSpan.Zero && clamped > session.NaturalDuration)
            {
                clamped = session.NaturalDuration;
            }

            session.Position = clamped;
            // PositionChanged is not raised while paused; keep the seek bar in sync.
            if (session.PlaybackState is MediaPlaybackState.Paused)
            {
                PositionChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(clamped, oldValue));
                UpdateChapter(clamped);
            }
        }
    }

    /// <inheritdoc/>
    public TimeSpan NaturalDuration => Session?.NaturalDuration ?? TimeSpan.Zero;

    /// <inheritdoc/>
    public ChapterCue? Chapter
    {
        get => _chapter;
        private set
        {
            if (ReferenceEquals(value, _chapter)) return;
            ChapterCue? oldValue = _chapter;
            _chapter = value;
            ChapterChanged?.Invoke(this, new ValueChangedEventArgs<ChapterCue?>(value, oldValue));
        }
    }

    /// <inheritdoc/>
    public double PlaybackRate
    {
        get => Session?.PlaybackRate ?? 1.0;
        set
        {
            MediaPlaybackSession? session = Session;
            if (session is null) return;
            double oldValue = session.PlaybackRate;
            session.PlaybackRate = value;
            PlaybackRateChanged?.Invoke(this, new ValueChangedEventArgs<double>(value, oldValue));
        }
    }

    /// <inheritdoc/>
    /// <value>Offset in milliseconds. Positive values delay the audio.</value>
    public double AudioDelay
    {
        get => _audioDelay;
        set
        {
            double oldValue = _audioDelay;
            _audioDelay = value;
            ApplyAudioDelay();
            AudioDelayChanged?.Invoke(this, new ValueChangedEventArgs<double>(value, oldValue));
        }
    }

    /// <inheritdoc/>
    /// <value>Offset in milliseconds. Positive values delay the subtitles.</value>
    public double SubtitleDelay
    {
        get => _subtitleDelay;
        set
        {
            double oldValue = _subtitleDelay;
            _subtitleDelay = value;
            ApplySubtitleDelay();
            SubtitleDelayChanged?.Invoke(this, new ValueChangedEventArgs<double>(value, oldValue));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Maps directly to <see cref="MediaPlaybackSession.NormalizedSourceRect"/>, which is the
    /// native pan/zoom equivalent of LibVLC's crop geometry.
    /// </remarks>
    public Rect NormalizedSourceRect
    {
        get => Session?.NormalizedSourceRect ?? new Rect(0, 0, 1, 1);
        set
        {
            MediaPlaybackSession? session = Session;
            if (session is null || session.NormalizedSourceRect == value) return;
            session.NormalizedSourceRect = value;
        }
    }

    /// <inheritdoc/>
    public IPlaybackItem? PlaybackItem
    {
        get => _playbackItem;
        set
        {
            if (ReferenceEquals(_playbackItem, value)) return;
            if (value is not null and not FFmpegPlaybackItem)
            {
                throw new NotSupportedException("Only FFmpegPlaybackItem is supported by FFmpegMediaPlayer.");
            }

            IPlaybackItem? oldValue = _playbackItem;
            if (_playbackItem is { } previous)
            {
                RemoveItemHandlers(previous);
            }

            _generation++;
            _ffmpegSource = null;

            if (value is null)
            {
                _playbackItem = null;
                _attachTask = null;
                _player.Source = null;
            }
            else
            {
                _playbackItem = (FFmpegPlaybackItem)value;
                RegisterItemHandlers(_playbackItem);
                _attachTask = AttachSourceAsync(_playbackItem, _generation);
            }

            PlaybackItemChanged?.Invoke(this, new ValueChangedEventArgs<IPlaybackItem?>(value, oldValue));
        }
    }

    private readonly NativeMediaPlayer _player;
    private FFmpegPlaybackItem? _playbackItem;
    private FFmpegMediaSource? _ffmpegSource;
    private Task? _attachTask;
    private ChapterCue? _chapter;
    private MediaPlaybackState _lastPlaybackState = MediaPlaybackState.None;
    private TimeSpan _lastPosition;
    private TimeSpan _lastNaturalDuration;
    private double _lastPlaybackRate = 1.0;
    private double _audioDelay;
    private double _subtitleDelay;
    private int _generation;
    private bool _isClosed;

    private MediaPlaybackSession? Session => _player.PlaybackSession;

    /// <summary>
    /// Creates the native media player and wires its events to the Screenbox abstractions.
    /// </summary>
    public FFmpegMediaPlayer()
    {
        _player = new NativeMediaPlayer
        {
            // Screenbox issues Play() explicitly (and may delay it while the queue settles).
            AutoPlay = false
        };

        _player.MediaOpened += OnMediaOpened;
        _player.MediaFailed += OnMediaFailed;
        _player.MediaEnded += OnMediaEnded;
        _player.VolumeChanged += OnVolumeChanged;
        _player.IsMutedChanged += OnIsMutedChanged;

        MediaPlaybackSession session = _player.PlaybackSession;
        session.PlaybackStateChanged += OnPlaybackStateChanged;
        session.PositionChanged += OnPositionChanged;
        session.NaturalDurationChanged += OnNaturalDurationChanged;
        session.NaturalVideoSizeChanged += OnNaturalVideoSizeChanged;
        session.BufferingProgressChanged += OnBufferingProgressChanged;
        session.BufferingStarted += OnBufferingStarted;
        session.BufferingEnded += OnBufferingEnded;
        session.PlaybackRateChanged += OnPlaybackRateChanged;
    }

    private async Task AttachSourceAsync(FFmpegPlaybackItem item, int generation)
    {
        try
        {
            FFmpegMediaSource source = await item.OpenTask;
            if (generation != _generation || !ReferenceEquals(_playbackItem, item))
            {
                return;
            }

            MediaPlaybackItem? playbackItem = item.MediaPlaybackItem;
            if (playbackItem is null) return;

            _ffmpegSource = source;
            // FastSeek needs the active session to resolve keyframe positions.
            source.PlaybackSession = _player.PlaybackSession;

            ApplyAudioDelay();
            ApplySubtitleDelay();
            item.AudioTracks.ApplySelection(item.AudioTracks.SelectedIndex);
            item.VideoTracks.ApplySelection(item.VideoTracks.SelectedIndex);
            item.SubtitleTracks.ApplySelection(item.SubtitleTracks.SelectedIndex);

            _player.Source = playbackItem;
        }
        catch (Exception)
        {
            if (generation == _generation)
            {
                MediaFailed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnMediaOpened(NativeMediaPlayer sender, object? args)
    {
        RefreshCapabilities();
        MediaOpened?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaFailed(NativeMediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        MediaFailed?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaEnded(NativeMediaPlayer sender, object? args)
    {
        MediaEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnVolumeChanged(NativeMediaPlayer sender, object? args)
    {
        VolumeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnIsMutedChanged(NativeMediaPlayer sender, object? args)
    {
        IsMutedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object? args)
    {
        MediaPlaybackState oldValue = _lastPlaybackState;
        _lastPlaybackState = sender.PlaybackState;
        RefreshCapabilities();
        PlaybackStateChanged?.Invoke(this, new ValueChangedEventArgs<MediaPlaybackState>(_lastPlaybackState, oldValue));
    }

    private void OnPositionChanged(MediaPlaybackSession sender, object? args)
    {
        TimeSpan position = sender.Position;
        TimeSpan oldValue = _lastPosition;
        _lastPosition = position;
        PositionChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(position, oldValue));
        UpdateChapter(position);
    }

    private void OnNaturalDurationChanged(MediaPlaybackSession sender, object? args)
    {
        TimeSpan oldValue = _lastNaturalDuration;
        _lastNaturalDuration = sender.NaturalDuration;
        RefreshCapabilities();
        NaturalDurationChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(_lastNaturalDuration, oldValue));
    }

    private void OnNaturalVideoSizeChanged(MediaPlaybackSession sender, object? args)
    {
        NaturalVideoSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBufferingProgressChanged(MediaPlaybackSession sender, object? args)
    {
        BufferingProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBufferingStarted(MediaPlaybackSession sender, object? args)
    {
        BufferingStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnBufferingEnded(MediaPlaybackSession sender, object? args)
    {
        BufferingEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaybackRateChanged(MediaPlaybackSession sender, object? args)
    {
        double oldValue = _lastPlaybackRate;
        _lastPlaybackRate = sender.PlaybackRate;
        PlaybackRateChanged?.Invoke(this, new ValueChangedEventArgs<double>(_lastPlaybackRate, oldValue));
    }

    private void RefreshCapabilities()
    {
        bool canSeek = Session?.CanSeek ?? false;
        if (CanSeek == canSeek) return;
        CanSeek = canSeek;
        CanSeekChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateChapter(TimeSpan position)
    {
        if (_playbackItem is not { Chapters: { Count: > 0 } chapters })
        {
            Chapter = null;
            return;
        }

        ChapterCue? current = null;
        foreach (ChapterCue cue in chapters)
        {
            if (cue.StartTime > position) break;
            current = cue;
        }

        Chapter = current;
    }

    private void RemoveItemHandlers(FFmpegPlaybackItem item)
    {
        item.AudioTracks.SelectedIndexChanged -= AudioTracksOnSelectedIndexChanged;
        item.VideoTracks.SelectedIndexChanged -= VideoTracksOnSelectedIndexChanged;
        item.SubtitleTracks.SelectedIndexChanged -= SubtitleTracksOnSelectedIndexChanged;
    }

    private void RegisterItemHandlers(FFmpegPlaybackItem item)
    {
        RemoveItemHandlers(item);
        item.AudioTracks.SelectedIndexChanged += AudioTracksOnSelectedIndexChanged;
        item.VideoTracks.SelectedIndexChanged += VideoTracksOnSelectedIndexChanged;
        item.SubtitleTracks.SelectedIndexChanged += SubtitleTracksOnSelectedIndexChanged;
    }

    private void AudioTracksOnSelectedIndexChanged(Windows.Media.Core.ISingleSelectMediaTrackList sender, object? args)
    {
        _playbackItem?.AudioTracks.ApplySelection(sender.SelectedIndex);
    }

    private void VideoTracksOnSelectedIndexChanged(Windows.Media.Core.ISingleSelectMediaTrackList sender, object? args)
    {
        _playbackItem?.VideoTracks.ApplySelection(sender.SelectedIndex);
    }

    private void SubtitleTracksOnSelectedIndexChanged(Windows.Media.Core.ISingleSelectMediaTrackList sender, object? args)
    {
        _playbackItem?.SubtitleTracks.ApplySelection(sender.SelectedIndex);
    }

    private void ApplyAudioDelay()
    {
        if (_ffmpegSource?.CurrentAudioStream is { } audioStream)
        {
            _ffmpegSource.SetStreamDelay(audioStream, TimeSpan.FromMilliseconds(_audioDelay));
        }
    }

    private void ApplySubtitleDelay()
    {
        _ffmpegSource?.SetSubtitleDelay(TimeSpan.FromMilliseconds(_subtitleDelay));
    }

    /// <inheritdoc/>
    public void Play()
    {
        _ = PlayAsync();
    }

    private async Task PlayAsync()
    {
        Task? attachTask = _attachTask;
        if (attachTask is not null)
        {
            await attachTask;
        }

        if (_isClosed || _playbackItem is null || _player.Source is null) return;
        _player.Play();
    }

    /// <inheritdoc/>
    public void Pause()
    {
        if (PlaybackState is MediaPlaybackState.Playing)
        {
            _player.Pause();
        }
    }

    /// <inheritdoc/>
    public void StepForwardOneFrame()
    {
        _player.StepForwardOneFrame();
    }

    /// <inheritdoc/>
    public void StepBackwardOneFrame()
    {
        _player.StepBackwardOneFrame();
    }

    /// <inheritdoc/>
    public void AddSubtitle(IStorageFile file, bool select = true)
    {
        _playbackItem?.SubtitleTracks.AddExternalSubtitle(file, select);
    }

    /// <inheritdoc/>
    public bool NextChapter()
    {
        IReadOnlyList<ChapterCue> chapters = _playbackItem?.Chapters ?? Array.Empty<ChapterCue>();
        if (chapters.Count == 0) return false;

        foreach (ChapterCue cue in chapters)
        {
            if (cue.StartTime > Position + TimeSpan.FromMilliseconds(250))
            {
                Position = cue.StartTime;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public bool PreviousChapter()
    {
        IReadOnlyList<ChapterCue> chapters = _playbackItem?.Chapters ?? Array.Empty<ChapterCue>();
        if (chapters.Count == 0) return false;

        TimeSpan threshold = Position - TimeSpan.FromSeconds(3);
        ChapterCue? previous = null;
        foreach (ChapterCue cue in chapters)
        {
            if (cue.StartTime >= threshold) break;
            previous = cue;
        }

        if (previous is null) return false;
        Position = previous.StartTime;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// FFmpegInteropX exposes frame extraction through the asynchronous FrameGrabber API,
    /// which does not fit this synchronous contract. Snapshot is therefore unavailable until
    /// the view model is migrated to an async command.
    /// </remarks>
    public bool TryTakeSnapshot(string destinationFolderPath, uint width = 0, uint height = 0)
    {
        return false;
    }

    /// <inheritdoc/>
    public void Close()
    {
        if (_isClosed) return;
        _isClosed = true;

        _player.Source = null;
        // The playback item owns the FFmpegMediaSource and the file stream. Disposing the
        // item (rather than the source directly) keeps a single owner, so a later
        // PlayQueueBufferManager.Clear() disposal is a safe no-op.
        _playbackItem?.Dispose();
        _playbackItem = null;
        _ffmpegSource = null;
        _player.Dispose();
    }
}
