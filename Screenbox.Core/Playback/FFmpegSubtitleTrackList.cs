using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFmpegInteropX;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Screenbox.Core.Playback;

/// <summary>
/// Exposes embedded and external subtitles reported by <see cref="FFmpegMediaSource.SubtitleStreams"/>
/// through Screenbox's subtitle track abstraction.
/// </summary>
public sealed partial class FFmpegSubtitleTrackList : SingleSelectTrackList<SubtitleTrack>, ISubtitleTrackList
{
    private readonly List<TimedMetadataTrack> _timedTracks = new();
    private FFmpegMediaSource? _source;
    private MediaPlaybackItem? _playbackItem;

    /// <summary>
    /// Populates the list from the FFmpeg source and remembers the objects needed for selection.
    /// </summary>
    internal void Attach(FFmpegMediaSource source, MediaPlaybackItem playbackItem)
    {
        _source = source;
        _playbackItem = playbackItem;
        TrackList.Clear();
        _timedTracks.Clear();

        int index = 0;
        foreach (SubtitleStreamInfo stream in source.SubtitleStreams)
        {
            string label = string.IsNullOrEmpty(stream.Name) ? stream.CodecName : stream.Name;
            TrackList.Add(new SubtitleTrack(index.ToString(), label, stream.Language ?? string.Empty));
            _timedTracks.Add(stream.SubtitleTrack);
            index++;
        }

        SelectedIndex = -1;
    }

    /// <summary>
    /// Applies the currently selected entry to the underlying timed metadata tracks.
    /// </summary>
    internal void ApplySelection(int index)
    {
        if (_playbackItem is null)
        {
            return;
        }

        for (int i = 0; i < _timedTracks.Count; i++)
        {
            int trackIndex = IndexOfTimedTrack(_timedTracks[i]);
            if (trackIndex < 0)
            {
                continue;
            }

            _playbackItem.TimedMetadataTracks.SetPresentationMode(
                (uint)trackIndex,
                i == index
                    ? TimedMetadataTrackPresentationMode.PlatformPresented
                    : TimedMetadataTrackPresentationMode.Disabled);
        }
    }

    /// <inheritdoc/>
    public override void Refresh()
    {
        ApplySelection(SelectedIndex);
    }

    /// <summary>
    /// Adds an external subtitle file at runtime using FFmpegInteropX's subtitle parser.
    /// </summary>
    public void AddExternalSubtitle(IStorageFile file, bool select = true)
    {
        _ = AddExternalSubtitleAsync(file, select);
    }

    private async Task AddExternalSubtitleAsync(IStorageFile file, bool select)
    {
        if (_source is null || _playbackItem is null)
        {
            return;
        }

        try
        {
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            await _source.AddExternalSubtitleAsync(stream, file.Name);

            // Rebuild the list so the newly parsed subtitle stream shows up in the picker.
            Attach(_source, _playbackItem);
            if (select && TrackList.Count > 0)
            {
                SelectedIndex = TrackList.Count - 1;
            }
        }
        catch (Exception)
        {
            // A malformed subtitle file must never tear down playback. The track list simply
            // stays unchanged and the user can retry with another file.
        }
    }

    private int IndexOfTimedTrack(TimedMetadataTrack track)
    {
        if (_playbackItem is null)
        {
            return -1;
        }

        for (int i = 0; i < _playbackItem.TimedMetadataTracks.Count; i++)
        {
            if (ReferenceEquals(_playbackItem.TimedMetadataTracks[i], track))
            {
                return i;
            }
        }

        // WinRT projections do not guarantee reference identity, so fall back to the track id.
        for (int i = 0; i < _playbackItem.TimedMetadataTracks.Count; i++)
        {
            if (_playbackItem.TimedMetadataTracks[i].Id == track.Id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Detaches the source so a disposed instance cannot be used for subtitle operations.
    /// </summary>
    internal void Detach()
    {
        _source = null;
        _playbackItem = null;
        _timedTracks.Clear();
        TrackList.Clear();
    }
}
