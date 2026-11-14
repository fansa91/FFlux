using Screenbox.Core.MediaSources;
using System;
using System.Threading.Tasks;
using Screenbox.Core.Helpers;
using Screenbox.Core.ViewModels;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;

namespace Screenbox.Core.Services;

/// <summary>
/// Wraps SystemMediaTransportControls for playback integration.
/// <para>
/// <c>SystemMediaTransportControls.GetForCurrentView()</c> requires a UWP
/// CoreApplication view backed by package identity and throws in an unpackaged
/// WinUI 3 desktop process. When that happens this service becomes inert:
/// <see cref="TransportControls"/> is <see langword="null"/> and every update is a
/// no-op, so media playback and startup are unaffected.
/// </para>
/// </summary>
public sealed class SystemMediaTransportControlsService : ISystemMediaTransportControlsService
{
    public SystemMediaTransportControls? TransportControls { get; }

    private DateTime _lastUpdated;

    public SystemMediaTransportControlsService()
    {
        try
        {
            SystemMediaTransportControls controls = SystemMediaTransportControls.GetForCurrentView();
            controls.IsEnabled = true;
            controls.IsPlayEnabled = true;
            controls.IsPauseEnabled = true;
            controls.IsStopEnabled = true;
            controls.AutoRepeatMode = MediaPlaybackAutoRepeatMode.None;
            controls.PlaybackStatus = MediaPlaybackStatus.Closed;
            controls.DisplayUpdater.ClearAll();
            TransportControls = controls;
        }
        catch (Exception)
        {
            // No SMTC in an unpackaged desktop process.
            TransportControls = null;
        }

        _lastUpdated = DateTime.MinValue;
    }

    public async Task UpdateTransportControlsDisplayAsync(MediaViewModel? item)
    {
        if (TransportControls is null) return;

        SystemMediaTransportControlsDisplayUpdater displayUpdater = TransportControls.DisplayUpdater;
        displayUpdater.ClearAll();
        displayUpdater.AppMediaId = "Screenbox";
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
                    bool success = await displayUpdater.CopyFromFileAsync(MediaPlaybackType.Music, file);
                    if (success && string.IsNullOrEmpty(displayUpdater.MusicProperties.Title))
                    {
                        displayUpdater.MusicProperties.Title = item.Name;
                    }
                }
                else if (file.IsSupportedVideo())
                {
                    bool success = await displayUpdater.CopyFromFileAsync(MediaPlaybackType.Video, file);
                    if (success && string.IsNullOrEmpty(displayUpdater.VideoProperties.Title))
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

            displayUpdater.Update();
        }
        catch (Exception)
        {
            // Some media types cannot be initialized; best effort only.
        }
    }

    public void UpdatePlaybackPosition(TimeSpan position, TimeSpan startTime, TimeSpan endTime)
    {
        if (TransportControls is null) return;
        if (DateTime.Now - _lastUpdated < TimeSpan.FromSeconds(5)) return;
        _lastUpdated = DateTime.Now;
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
