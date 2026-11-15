using System;
using System.Collections.Generic;
using Screenbox.Core.Events;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Watches for available casting renderers.
/// </summary>
/// <remarks>
/// LibVLC exposed renderer discovery on its own LibVLC instance. The native
/// Windows.Media.Playback.MediaPlayer does not expose that API; casting must be migrated
/// to <c>MediaPlayer.GetAsCastingSource()</c> with a DevicePicker. Until then the watcher
/// reports no renderers so the cast UI stays inert instead of throwing.
/// </remarks>
public sealed partial class RendererWatcher : IDisposable
{
#pragma warning disable CS0067 // Kept for the cast view model's event subscription contract.
    public event EventHandler<RendererFoundEventArgs>? RendererFound;
    public event EventHandler<RendererLostEventArgs>? RendererLost;
#pragma warning restore CS0067

    public bool IsStarted { get; private set; }

    private readonly List<Renderer> _renderers = new();

    internal RendererWatcher(IMediaPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
    }

    public IReadOnlyList<Renderer> GetRenderers()
    {
        return _renderers.AsReadOnly();
    }

    public bool Start()
    {
        IsStarted = true;
        return true;
    }

    public void Stop()
    {
        IsStarted = false;
        _renderers.Clear();
    }

    public void Dispose()
    {
        Stop();
    }
}
