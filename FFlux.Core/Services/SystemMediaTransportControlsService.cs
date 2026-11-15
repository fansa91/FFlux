using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Screenbox.Core.Contexts;
using Screenbox.Core.Helpers;
using Screenbox.Core.MediaSources;
using Screenbox.Core.Messages;
using Screenbox.Core.Playback;
using Screenbox.Core.ViewModels;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using NativeMediaPlayer = Windows.Media.Playback.MediaPlayer;
using TrackNavigationDirection = Screenbox.Core.Enums.TrackNavigationDirection;

namespace Screenbox.Core.Services;

/// <summary>
/// Publishes playback to the Windows system media transport controls: the flyout above the
/// volume slider, the lock screen, hardware media keys and Bluetooth headset buttons.
/// </summary>
/// <remarks>
/// <para>
/// This used to call <c>SystemMediaTransportControls.GetForCurrentView()</c>, which needs a UWP
/// CoreApplication view. In an unpackaged WinUI 3 process that throws
/// <c>COMException 0x80070578 "Invalid window handle"</c>, so the service silently degraded to
/// a no-op: the app played fine but never appeared in the system flyout, and media keys did
/// nothing.
/// </para>
/// <para>
/// The owner of the session for a desktop app is the player itself.
/// <c>MediaPlayer.SystemMediaTransportControls</c> returns the instance the player already
/// owns, and it works without package identity. The service therefore takes the native player
/// instead of looking for a view.
/// </para>
/// </remarks>
public sealed class SystemMediaTransportControlsService : ISystemMediaTransportControlsService
{
    public SystemMediaTransportControls? TransportControls { get; }

    /// <summary>
    /// The flyout's progress bar is driven by <c>UpdateTimelineProperties</c>; one update a
    /// second keeps it moving without pushing work into the UI thread on every tick.
    /// </summary>
    private static readonly TimeSpan PositionUpdateInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Serializes display updates: the queue can change faster than artwork and tags load.
    /// </summary>
    private readonly SemaphoreSlim _displayLock = new(1, 1);

    private readonly PlayQueueContext? _queue;

    private readonly PlayerContext? _playerContext;

    private DateTime _lastPositionUpdate;

    public SystemMediaTransportControlsService(
        NativeMediaPlayer player,
        PlayQueueContext? queue = null,
        PlayerContext? playerContext = null)
    {
        _lastPositionUpdate = DateTime.MinValue;
        _queue = queue;
        _playerContext = playerContext;

        try
        {
            SystemMediaTransportControls controls = player.SystemMediaTransportControls;
            controls.IsEnabled = true;
            controls.IsPlayEnabled = true;
            controls.IsPauseEnabled = true;
            controls.IsStopEnabled = true;
            controls.IsNextEnabled = true;
            controls.IsPreviousEnabled = true;
            controls.IsFastForwardEnabled = true;
            controls.IsRewindEnabled = true;
            controls.AutoRepeatMode = MediaPlaybackAutoRepeatMode.None;
            controls.PlaybackStatus = MediaPlaybackStatus.Closed;
            controls.DisplayUpdater.ClearAll();
            TransportControls = controls;
        }
        catch (Exception)
        {
            // Without a session every update below becomes a no-op; playback is unaffected.
            TransportControls = null;
        }

        if (_queue is not null)
        {
            _queue.PropertyChanged += OnQueuePropertyChanged;
        }

        // The session lives as long as the player, so the button handlers belong here rather
        // than on a view model: PlayerElementViewModel is transient and used to subscribe on
        // every construction without ever unsubscribing, which would fire each media key once
        // per created view.
        if (TransportControls is { } session)
        {
            session.ButtonPressed += OnButtonPressed;
            session.PlaybackPositionChangeRequested += OnPlaybackPositionChangeRequested;
        }
    }

    private void OnPlaybackPositionChangeRequested(
        SystemMediaTransportControls sender,
        PlaybackPositionChangeRequestedEventArgs args)
    {
        if (_playerContext?.MediaPlayer is { } player)
        {
            player.Position = args.RequestedPlaybackPosition;
        }
    }

    private void OnButtonPressed(
        SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        IMediaPlayer? player = _playerContext?.MediaPlayer;

        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                player?.Play();
                break;
            case SystemMediaTransportControlsButton.Pause:
                player?.Pause();
                break;
            case SystemMediaTransportControlsButton.Stop:
                if (player is not null) player.PlaybackItem = null;
                break;
            case SystemMediaTransportControlsButton.FastForward:
                if (player is not null) player.Position += TimeSpan.FromSeconds(10);
                break;
            case SystemMediaTransportControlsButton.Rewind:
                if (player is not null) player.Position -= TimeSpan.FromSeconds(10);
                break;
            // Track navigation goes through the messenger so it follows the play queue and the
            // same path the in-app buttons use.
            case SystemMediaTransportControlsButton.Next:
                WeakReferenceMessenger.Default.Send(new TrackNavigationMessage(TrackNavigationDirection.Next));
                break;
            case SystemMediaTransportControlsButton.Previous:
                WeakReferenceMessenger.Default.Send(new TrackNavigationMessage(TrackNavigationDirection.Previous));
                break;
        }
    }

    /// <summary>
    /// Keeps the flyout in step with the queue instead of relying on the playback coordinator's
    /// post-processing, which returns early whenever the current item moved on before it ran.
    /// </summary>
    private void OnQueuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayQueueContext.CurrentItem)) return;
        _ = RefreshDisplayAsync(_queue?.CurrentItem);
    }

    private async Task RefreshDisplayAsync(MediaViewModel? item)
    {
        try
        {
            await UpdateTransportControlsDisplayAsync(item);
        }
        catch (Exception)
        {
            // The session can be torn down while the queue is changing; the next item retries.
            return;
        }

        // Artist tags are parsed a moment after the item becomes current, so the first pass can
        // publish a title with no artist. Retry once, and only when something is still missing.
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1200));
            if (_queue?.CurrentItem != item) return;
            if (TransportControls?.DisplayUpdater.MusicProperties.Artist is { Length: > 0 }) return;
            await UpdateTransportControlsDisplayAsync(item);
        }
        catch (Exception)
        {
            // Best effort only.
        }
    }

    public async Task UpdateTransportControlsDisplayAsync(MediaViewModel? item)
    {
        await _displayLock.WaitAsync();
        try
        {
            await UpdateDisplayCoreAsync(item);
        }
        finally
        {
            _displayLock.Release();
        }
    }

    private async Task UpdateDisplayCoreAsync(MediaViewModel? item)
    {
        if (TransportControls is null) return;

        SystemMediaTransportControlsDisplayUpdater displayUpdater = TransportControls.DisplayUpdater;
        displayUpdater.ClearAll();
        displayUpdater.AppMediaId = "FFlux";
        if (item == null)
        {
            return;
        }

        try
        {
            if (item.Source is IFileMediaSource fileSource && await fileSource.GetStorageFileAsync() is { } file)
            {
                if (file.IsSupportedAudio())
                {
                    // Pin the type first: if the tag reader below throws, the fallback must not
                    // mistake a music file for a video one.
                    displayUpdater.Type = MediaPlaybackType.Music;
                    try
                    {
                        await displayUpdater.CopyFromFileAsync(MediaPlaybackType.Music, file);
                    }
                    catch (Exception)
                    {
                        // Unreadable or absent tags; the view model still supplies a title.
                    }

                    if (string.IsNullOrEmpty(displayUpdater.MusicProperties.Title))
                    {
                        displayUpdater.MusicProperties.Title = item.Name;
                    }

                    // Tags are often incomplete; the library view model still knows the artists.
                    if (string.IsNullOrEmpty(displayUpdater.MusicProperties.Artist) && item.Artists is { Length: > 0 })
                    {
                        displayUpdater.MusicProperties.Artist = string.Join(", ", item.Artists.Select(a => a.Name));
                    }
                }
                else if (file.IsSupportedVideo())
                {
                    displayUpdater.Type = MediaPlaybackType.Video;
                    try
                    {
                        await displayUpdater.CopyFromFileAsync(MediaPlaybackType.Video, file);
                    }
                    catch (Exception)
                    {
                        // Same as above for the video property set.
                    }

                    if (string.IsNullOrEmpty(displayUpdater.VideoProperties.Title))
                    {
                        displayUpdater.VideoProperties.Title = item.Name;
                    }
                }
            }

            // DisplayUpdater can only have type of Video, Audio, or Image
            if (displayUpdater.Type == MediaPlaybackType.Unknown)
            {
                displayUpdater.Type = MediaPlaybackType.Video;
                displayUpdater.VideoProperties.Title = item.Name;
            }

            // CopyFromFileAsync picks up an embedded cover, but a loose cover.jpg next to the
            // file, or a frame extracted from a video, only exists in the view model.
            if (displayUpdater.Thumbnail is null)
            {
                await ApplyThumbnailAsync(displayUpdater, item);
            }

            displayUpdater.Update();
        }
        catch (Exception)
        {
            // Some media types cannot be initialized; best effort only.
        }
    }

    /// <summary>
    /// Publishes cover art to the flyout from the artwork the library already resolved.
    /// </summary>
    /// <remarks>
    /// The shell thumbnail route is wrong for audio: every mode returns the generic file-type
    /// icon, so the flyout would show a grey glyph instead of the album cover. The bytes come
    /// from the container (already extracted while playing) or from a cover image sitting next
    /// to the file.
    /// </remarks>
    private static async Task ApplyThumbnailAsync(
        SystemMediaTransportControlsDisplayUpdater displayUpdater,
        MediaViewModel item)
    {
        try
        {
            byte[]? artwork = item.MediaInfo is { } mediaInfo ? mediaInfo.ArtworkData : null;
            if (artwork is not { Length: > 0 }
                && item.Source is IFileMediaSource fileSource
                && await fileSource.GetStorageFileAsync() is { } file)
            {
                artwork = await AlbumArtworkLoader.LoadAsync(file);
            }

            if (artwork is not { Length: > 0 }) return;

            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(artwork);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            displayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromStream(stream);
        }
        catch (Exception)
        {
            // A missing thumbnail only costs the flyout its artwork.
        }
    }

    public void UpdatePlaybackPosition(TimeSpan position, TimeSpan startTime, TimeSpan endTime)
    {
        if (TransportControls is null) return;
        if (DateTime.Now - _lastPositionUpdate < PositionUpdateInterval) return;
        _lastPositionUpdate = DateTime.Now;

        SystemMediaTransportControlsTimelineProperties timelineProps = new()
        {
            StartTime = startTime,
            MinSeekTime = startTime,
            Position = position,
            MaxSeekTime = endTime,
            EndTime = endTime
        };

        TransportControls.UpdateTimelineProperties(timelineProps);
    }

    public void UpdatePlaybackStatus(MediaPlaybackState state)
    {
        if (TransportControls is null) return;

        switch (state)
        {
            case MediaPlaybackState.None:
                TransportControls.PlaybackStatus = MediaPlaybackStatus.Stopped;
                break;
            case MediaPlaybackState.Opening:
            case MediaPlaybackState.Buffering:
                TransportControls.PlaybackStatus = MediaPlaybackStatus.Changing;
                break;
            case MediaPlaybackState.Playing:
                TransportControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                break;
            case MediaPlaybackState.Paused:
                TransportControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                break;
            default:
                break;
        }
    }

    public void ClosePlayback()
    {
        if (TransportControls is null) return;

        TransportControls.PlaybackStatus = MediaPlaybackStatus.Closed;
        TransportControls.DisplayUpdater.ClearAll();
    }
}
