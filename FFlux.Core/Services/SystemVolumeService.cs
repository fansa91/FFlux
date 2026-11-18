using System;
using System.Runtime.InteropServices.Marshalling;

namespace Screenbox.Core.Services;

/// <summary>
/// Reads and writes the default Windows render endpoint's master volume through WASAPI.
/// </summary>
/// <remarks>
/// COM objects are created and used only on the calling thread. The app constructs and polls
/// this service from the UI dispatcher (STA), so no explicit apartment marshalling is needed.
/// Nothing is cached across calls beyond the enumerator: the default endpoint is resolved fresh
/// each time so a device switch (headset plugged in, Bluetooth connect) is picked up immediately,
/// and a transient COM failure simply reports "unavailable" rather than throwing into UI code.
/// </remarks>
public sealed class SystemVolumeService : ISystemVolumeService
{
    private readonly object _gate = new();
    private IMMDeviceEnumerator? _enumerator;

    public bool TryGetVolume(out int volume)
    {
        volume = 0;
        try
        {
            lock (_gate)
            {
                if (GetEndpointVolume() is not { } endpoint) return false;
                int hr = endpoint.GetMasterVolumeLevelScalar(out float scalar);
                if (hr < 0) return false;
                volume = (int)Math.Round(Math.Clamp(scalar, 0f, 1f) * 100f);
                return true;
            }
        }
        catch (Exception)
        {
            // No default device, device removed mid-call, or COM not initialized.
            return false;
        }
    }

    public bool TrySetVolume(int volume)
    {
        float scalar = Math.Clamp(volume, 0, 100) / 100f;
        try
        {
            lock (_gate)
            {
                if (GetEndpointVolume() is not { } endpoint) return false;
                return endpoint.SetMasterVolumeLevelScalar(scalar, 0) >= 0;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryGetMute(out bool mute)
    {
        mute = false;
        try
        {
            lock (_gate)
            {
                if (GetEndpointVolume() is not { } endpoint) return false;
                int hr = endpoint.GetMute(out int value);
                if (hr < 0) return false;
                mute = value != 0;
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TrySetMute(bool mute)
    {
        try
        {
            lock (_gate)
            {
                if (GetEndpointVolume() is not { } endpoint) return false;
                return endpoint.SetMute(mute ? 1 : 0, 0) >= 0;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private unsafe IAudioEndpointVolume? GetEndpointVolume()
    {
        try
        {
            _enumerator ??= CreateEnumerator();
            if (_enumerator is null) return null;

            int hr = _enumerator.GetDefaultAudioEndpoint(
                Wasapi.EDataFlowRender, Wasapi.ERoleMultimedia, out IMMDevice device);
            if (hr < 0) return null;

            GuidNative iid = GuidNative.From(Wasapi.IIdIAudioEndpointVolume);
            hr = device.Activate(iid, Wasapi.ClsCtxAll, 0, out nint pointer);
            if (hr < 0 || pointer == 0) return null;

            return ComInterfaceMarshaller<IAudioEndpointVolume>.ConvertToManaged((void*)pointer);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IMMDeviceEnumerator? CreateEnumerator()
    {
        int hr = Wasapi.CoCreateInstance(
            Wasapi.ClsIdMMDeviceEnumerator, 0, Wasapi.ClsCtxAll,
            typeof(IMMDeviceEnumerator).GUID, out IMMDeviceEnumerator enumerator);
        return hr >= 0 ? enumerator : null;
    }
}
