using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Screenbox.Core.MediaSources;
using Screenbox.Core.Playback;

namespace Screenbox.Core.Services;

/// <summary>
/// Creates the FFmpeg backed player and playback items.
/// </summary>
/// <remarks>
/// LibVLC required a native instance and a D3D swap chain per video view. The native
/// <c>Windows.Media.Playback.MediaPlayer</c> is a long lived object owned by the DI
/// container, so initialization here only records global options and returns that instance.
/// </remarks>
public sealed class PlayerService : IPlayerService
{
    private readonly IMediaPlayer _mediaPlayer;
    private readonly ILogger<PlayerService> _logger;
    private readonly object _optionsLock = new();
    private string[] _globalOptions = [];

    /// <summary>
    /// Creates the player service.
    /// </summary>
    public PlayerService(
        IMediaPlayer mediaPlayer,
        ILogger<PlayerService> logger)
    {
        _mediaPlayer = mediaPlayer;
        _logger = logger;
    }

    /// <inheritdoc/>
    public IMediaPlayer Initialize(string[] swapChainOptions)
    {
        // The parameter is kept for compatibility with the existing IPlayerService contract.
        // These tokens are now global FFmpeg options rather than LibVLC command-line switches.
        string[] options = swapChainOptions ?? [];
        lock (_optionsLock)
        {
            _globalOptions = options;
        }

        _logger.LogDebug("FFmpeg playback backend initialized with {OptionCount} global options.", options.Length);
        return _mediaPlayer;
    }

    /// <inheritdoc/>
    public IPlaybackItem CreatePlaybackItem(IMediaPlayer player, IMediaSource source, params string[] options)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (player is not FFmpegMediaPlayer)
        {
            throw new NotSupportedException("Only FFmpegMediaPlayer is supported.");
        }

        string[] globalOptions;
        lock (_optionsLock)
        {
            globalOptions = _globalOptions;
        }

        IEnumerable<string> merged = globalOptions.Concat(options ?? []);
        return new FFmpegPlaybackItem(source, merged);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The player is a DI singleton shared with the video view; it is closed when the
    /// application shuts down rather than on every view (un)load.
    /// </remarks>
    public void DisposePlayer(IMediaPlayer player)
    {
        // Intentionally a no-op: disposing the singleton would detach MediaPlayerElement.
    }
}
