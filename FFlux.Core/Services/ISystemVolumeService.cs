namespace Screenbox.Core.Services;

/// <summary>
/// Reads and writes the Windows system master (render endpoint) volume.
/// </summary>
/// <remarks>
/// All methods are best-effort: they return <see langword="false"/> when there is no default
/// audio endpoint (headless machine, disabled device, Remote Desktop without audio). Callers
/// must fall back to their own in-process volume in that case.
/// </remarks>
public interface ISystemVolumeService
{
    /// <summary>Reads the master volume as a percentage (0-100).</summary>
    bool TryGetVolume(out int volume);

    /// <summary>Sets the master volume from a percentage (clamped to 0-100).</summary>
    bool TrySetVolume(int volume);

    /// <summary>Reads the master mute state.</summary>
    bool TryGetMute(out bool mute);

    /// <summary>Sets the master mute state.</summary>
    bool TrySetMute(bool mute);
}
