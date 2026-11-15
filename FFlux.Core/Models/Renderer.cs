namespace Screenbox.Core.Models;

/// <summary>
/// Describes a media casting target.
/// </summary>
/// <remarks>
/// Renderer discovery was provided by LibVLC's RendererDiscoverer. The FFmpegInteropX
/// backend plays through Windows.Media.Playback, which casts via CastingSource/DevicePicker
/// instead of VLC renderer items. The model is retained so the cast UI keeps compiling
/// until that flow is implemented; no VLC native handle is stored.
/// </remarks>
public sealed partial class Renderer
{
    public bool IsAvailable { get; private set; }

    public string Name { get; }

    public string Type { get; }

    public string? IconUri { get; }

    public bool CanRenderVideo { get; }

    public bool CanRenderAudio { get; }

    internal Renderer(string name, string type, string? iconUri = null, bool canRenderVideo = true, bool canRenderAudio = true)
    {
        Name = name;
        Type = type;
        IconUri = iconUri;
        CanRenderVideo = canRenderVideo;
        CanRenderAudio = canRenderAudio;
        IsAvailable = true;
    }

    internal void Dispose()
    {
        IsAvailable = false;
    }

    public override string ToString()
    {
        return $"{Name}, {Type}";
    }
}
