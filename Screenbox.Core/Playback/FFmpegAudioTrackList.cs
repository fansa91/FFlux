using FFmpegInteropX;
using Windows.Media.Playback;

namespace Screenbox.Core.Playback;

/// <summary>
/// Exposes the audio streams reported by <see cref="FFmpegMediaSource.AudioStreams"/> through
/// Screenbox's track selection abstraction.
/// </summary>
public sealed partial class FFmpegAudioTrackList : SingleSelectTrackList<AudioTrack>
{
    private MediaPlaybackItem? _playbackItem;

    /// <summary>
    /// Populates the list from the FFmpeg source and remembers the playback item used for selection.
    /// </summary>
    internal void Attach(FFmpegMediaSource source, MediaPlaybackItem playbackItem)
    {
        _playbackItem = playbackItem;
        TrackList.Clear();

        int index = 0;
        foreach (AudioStreamInfo stream in source.AudioStreams)
        {
            string name = string.IsNullOrEmpty(stream.Name) ? stream.CodecName : stream.Name;
            TrackList.Add(new AudioTrack(index.ToString(), name, stream.Name, stream.Language ?? string.Empty));
            index++;
        }

        SelectedIndex = TrackList.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Applies the currently selected entry to the underlying <see cref="MediaPlaybackItem"/>.
    /// </summary>
    internal void ApplySelection(int index)
    {
        if (_playbackItem is null || index < 0 || index >= _playbackItem.AudioTracks.Count)
        {
            return;
        }

        _playbackItem.AudioTracks.SelectedIndex = index;
    }

    /// <inheritdoc/>
    public override void Refresh()
    {
        ApplySelection(SelectedIndex);
    }

    /// <summary>
    /// Detaches the playback item so a disposed source cannot be used for selection.
    /// </summary>
    internal void Detach()
    {
        _playbackItem = null;
        TrackList.Clear();
    }
}
