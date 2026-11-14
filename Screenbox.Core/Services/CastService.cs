using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;

namespace Screenbox.Core.Services;

/// <summary>
/// Casting service for the FFmpeg playback backend.
/// </summary>
public sealed class CastService : ICastService
{
    /// <inheritdoc/>
    public RendererWatcher CreateRendererWatcher(IMediaPlayer player)
    {
        return new RendererWatcher(player);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Renderer selection has no FFmpegInteropX equivalent. It will be reimplemented with
    /// <c>MediaPlayer.GetAsCastingSource()</c>; until then this always reports failure.
    /// </remarks>
    public bool SetActiveRenderer(IMediaPlayer player, Renderer? renderer)
    {
        return false;
    }
}
