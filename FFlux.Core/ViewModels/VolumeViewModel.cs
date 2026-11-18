using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using Windows.System;

namespace Screenbox.Core.ViewModels;

public sealed partial class VolumeViewModel : ObservableRecipient,
    IRecipient<SettingChangedMessage<PlaybackSettingsContext, int>>,
    IRecipient<ChangeVolumeRequestMessage>,
    IRecipient<PropertyChangedMessage<IMediaPlayer?>>
{
    // How often external system-volume changes are picked up. Polling on the UI dispatcher
    // keeps every WASAPI call on the STA thread and avoids a COM callback on an MTA worker,
    // which in this app previously surfaced as fatal stowed exceptions.
    private static readonly TimeSpan SystemVolumePollInterval = TimeSpan.FromMilliseconds(700);
    private const int SystemVolumeMax = 100;

    [ObservableProperty] public partial int MaxVolume { get; set; }
    [ObservableProperty] public partial int Volume { get; set; }
    [ObservableProperty] public partial bool IsMute { get; set; }

    private IMediaPlayer? MediaPlayer => _playerContext.MediaPlayer;

    private readonly IAppDispatcher _dispatcherQueue;
    private readonly PlaybackSettingsContext _playbackSettings;
    private readonly PlayerContext _playerContext;
    private readonly ISystemVolumeService _systemVolume;
    private readonly IAppDispatcherTimer _systemVolumeTimer;
    private bool _suppressSystemWrite;

    public VolumeViewModel(
        PlaybackSettingsContext playbackSettings,
        PlayerContext playerContext,
        ISystemVolumeService systemVolume)
    {
        _playbackSettings = playbackSettings;
        _playerContext = playerContext;
        _systemVolume = systemVolume;
        _dispatcherQueue = AppDispatcher.Current;

        MaxVolume = playbackSettings.MaxVolume;

        // Adopt the system master volume as the source of truth when a render endpoint exists.
        // Suppress the write-back from BOTH initial assignments so startup does not push our
        // persisted value over the system (which would also make the adoption read back 100).
        _suppressSystemWrite = true;
        Volume = playbackSettings.Volume;
        if (_systemVolume.TryGetVolume(out int currentSystemVolume))
        {
            Volume = Math.Clamp(currentSystemVolume, 0, MaxVolume);
        }

        if (_systemVolume.TryGetMute(out bool systemMute))
        {
            IsMute = systemMute;
        }
        else
        {
            IsMute = Volume == 0;
        }

        _suppressSystemWrite = false;
        ApplyPlayerGain();

        if (MediaPlayer != null)
        {
            MediaPlayer.VolumeChanged += OnVolumeChanged;
            MediaPlayer.IsMutedChanged += OnIsMutedChanged;
        }

        Messenger.Register<SettingChangedMessage<PlaybackSettingsContext, int>>(this);
        Messenger.Register<ChangeVolumeRequestMessage>(this);
        Messenger.Register<PropertyChangedMessage<IMediaPlayer?>>(this);

        _systemVolumeTimer = _dispatcherQueue.CreateTimer();
        _systemVolumeTimer.Interval = SystemVolumePollInterval;
        _systemVolumeTimer.IsRepeating = true;
        _systemVolumeTimer.Tick += OnSystemVolumeTimerTick;
        _systemVolumeTimer.Start();
    }

    public void Receive(SettingChangedMessage<PlaybackSettingsContext, int> message)
    {
        if (message.PropertyName == nameof(PlaybackSettingsContext.MaxVolume))
        {
            MaxVolume = message.NewValue;
            if (Volume > MaxVolume) Volume = MaxVolume;
        }
    }

    public void Receive(PropertyChangedMessage<IMediaPlayer?> message)
    {
        if (message.Sender is not PlayerContext) return;

        if (message.OldValue is { } oldPlayer)
        {
            oldPlayer.VolumeChanged -= OnVolumeChanged;
            oldPlayer.IsMutedChanged -= OnIsMutedChanged;
        }

        if (MediaPlayer != null)
        {
            MediaPlayer.VolumeChanged += OnVolumeChanged;
            MediaPlayer.IsMutedChanged += OnIsMutedChanged;
            ApplyPlayerGain();
        }
    }

    public void Receive(ChangeVolumeRequestMessage message)
    {
        SetVolume(message.Value, message.IsOffset);
        message.Reply(Volume);
    }

    /// <summary>
    /// Called when the slider changes. The slider drives the system master volume up to 100%;
    /// above that (the volume-boost setting) the extra gain stays local to the player, since a
    /// system endpoint cannot exceed unity.
    /// </summary>
    partial void OnVolumeChanged(int value)
    {
        _playbackSettings.Volume = value;
        if (value > 0) IsMute = false;

        if (!_suppressSystemWrite)
        {
            _systemVolume.TrySetVolume(Math.Min(value, SystemVolumeMax));
        }

        ApplyPlayerGain();
    }

    partial void OnIsMuteChanged(bool value)
    {
        if (!_suppressSystemWrite)
        {
            _systemVolume.TrySetMute(value);
        }

        _dispatcherQueue.TryExecuteGuarded(() =>
        {
            if (MediaPlayer is not null) MediaPlayer.IsMuted = value;
        });
    }

    /// <summary>
    /// Handles toggling the mute state.
    /// </summary>
    public void HandleMuteToggleKey()
    {
        IsMute = !IsMute;
        Messenger.Send(
            new PlayerOsdUpdateMessage(
                IsMute ? PlaybackCommandKind.Mute : PlaybackCommandKind.Volume,
                value: Volume)
            .ShowBadge());
    }

    /// <summary>
    /// Mirrors the player's own volume changes back into the view model. When the system
    /// endpoint is the source of truth the player is expected to stay at unity (or at the boost
    /// gain), so the comparison uses that value instead of Volume/100.
    /// </summary>
    private void OnVolumeChanged(IMediaPlayer sender, object? args)
    {
        // MediaPlayer.VolumeChanged fires on an MTA worker. Only read the scalar here;
        // everything else (including the WASAPI-backed gain lookup) runs on the UI thread.
        double playerVolume = sender.Volume;
        _dispatcherQueue.TryExecuteGuarded(() =>
        {
            double expected = GetPlayerGain();
            if (Math.Abs(playerVolume - expected) > 0.001)
            {
                sender.Volume = expected;
            }
        });
    }

    private void OnIsMutedChanged(IMediaPlayer sender, object? args)
    {
        // Same MTA rule: capture the scalar first, mutate only on the UI thread.
        bool playerMuted = sender.IsMuted;
        _dispatcherQueue.TryExecuteGuarded(() =>
        {
            if (playerMuted != IsMute)
            {
                sender.IsMuted = IsMute;
            }
        });
    }

    /// <summary>
    /// Picks up system-volume changes made outside the app (tray icon, keyboard media keys,
    /// another app). Runs on the UI dispatcher so every WASAPI call stays on the STA thread.
    /// </summary>
    private void OnSystemVolumeTimerTick(object? sender, EventArgs e)
    {
        if (!_systemVolume.TryGetVolume(out int systemVolume)) return;

        // While boost is active the slider sits above 100 but the system is pinned at 100.
        int expectedSystemVolume = Math.Min(Volume, SystemVolumeMax);
        if (systemVolume != expectedSystemVolume)
        {
            _suppressSystemWrite = true;
            Volume = Math.Clamp(systemVolume, 0, MaxVolume);
            _suppressSystemWrite = false;
        }

        if (_systemVolume.TryGetMute(out bool systemMute) && systemMute != IsMute)
        {
            _suppressSystemWrite = true;
            IsMute = systemMute;
            _suppressSystemWrite = false;
        }
    }

    /// <summary>
    /// Volume above 100 is an app-side boost; 0-100 is handled by the system master volume, so
    /// the player itself must stay at unity to avoid attenuating twice. Without a usable system
    /// endpoint, the player keeps full control as before.
    /// </summary>
    private double GetPlayerGain()
    {
        if (_systemVolume.TryGetVolume(out _))
        {
            return Volume > SystemVolumeMax ? Volume / 100d : 1.0;
        }

        return Volume / 100d;
    }

    private void ApplyPlayerGain()
    {
        IMediaPlayer? player = MediaPlayer;
        if (player is null) return;

        double gain = GetPlayerGain();
        if (Math.Abs(player.Volume - gain) > 0.001)
        {
            _dispatcherQueue.TryExecuteGuarded(() => player.Volume = gain);
        }
    }

    /// <summary>
    /// Sets the volume to a specified value or adjusts it by a given amount.
    /// </summary>
    /// <param name="value">The target volume to set or the offset amount to adjust.</param>
    /// <param name="isOffset">If <see langword="true"/>, adjusts the current volume by the specified <paramref name="value"/>;
    /// otherwise, sets the volume directly. The default value is <see langword="false"/>.</param>
    public void SetVolume(int value, bool isOffset = false)
    {
        Volume = Math.Clamp(isOffset ? Volume + value : value, 0, MaxVolume);
    }
}
