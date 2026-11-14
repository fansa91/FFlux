using CommunityToolkit.Mvvm.ComponentModel;
using Screenbox.Core.Playback;

namespace Screenbox.Core.Contexts;

public sealed partial class PlayerContext : ObservableRecipient
{
    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    public partial IMediaPlayer? MediaPlayer { get; set; }

    /// <summary>
    /// Seeds the context with the DI owned player so playback requests never race
    /// against the video view being loaded.
    /// </summary>
    public PlayerContext(IMediaPlayer mediaPlayer)
    {
        MediaPlayer = mediaPlayer;
    }
}
