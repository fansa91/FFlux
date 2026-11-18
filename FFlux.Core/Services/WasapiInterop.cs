using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Screenbox.Core.Services;

/// <summary>
/// Minimal WASAPI surface needed to read/write the default render endpoint's master volume.
/// </summary>
/// <remarks>
/// Uses source-generated COM (<see cref="GeneratedComInterfaceAttribute"/>) instead of
/// <c>ComImport</c> because the release build is Native AOT, where built-in COM marshalling is
/// unavailable. The vtable order must match the native interface, so every method up to the last
/// one used is declared in order.
///
/// These interfaces are only ever touched from the UI thread (see <see cref="SystemVolumeService"/>),
/// which is already STA-initialized by WinUI. No COM change callback is registered: callbacks
/// arrive on an MTA worker and escaping exceptions there become fatal stowed exceptions in this
/// app, so external changes are detected by polling instead.
/// </remarks>
internal static partial class Wasapi
{
    internal const uint EDataFlowRender = 0;
    internal const uint ERoleMultimedia = 0;
    internal const uint ClsCtxAll = 0x17;

    internal static readonly Guid ClsIdMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    internal static readonly Guid IIdIAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(
        in Guid clsid,
        nint outer,
        uint clsContext,
        in Guid iid,
        out IMMDeviceEnumerator instance);
}

/// <summary>
/// Blittable GUID layout. Source-generated COM cannot marshal <see cref="Guid"/> directly, so
/// IIDs crossing the interop boundary use this 16-byte value instead.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GuidNative
{
    private uint _data1;
    private ushort _data2;
    private ushort _data3;
    private ulong _data4;

    public static GuidNative From(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes);
        return MemoryMarshal.Read<GuidNative>(bytes);
    }
}

[GeneratedComInterface]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(uint dataFlow, uint stateMask, out nint devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(uint dataFlow, uint role, out IMMDevice device);
}

[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    // Activate is the first method after IUnknown; only it is needed, and later vtable
    // slots may be omitted by the source generator.
    [PreserveSig]
    int Activate(in GuidNative iid, uint clsContext, nint activationParams, out nint interfacePointer);
}

[GeneratedComInterface]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal partial interface IAudioEndpointVolume
{
    // vtable order: IUnknown(3) + the following methods in declaration order.
    [PreserveSig]
    int RegisterControlChangeNotify(nint callback);

    [PreserveSig]
    int UnregisterControlChangeNotify(nint callback);

    [PreserveSig]
    int GetChannelCount(out uint channelCount);

    [PreserveSig]
    int GetMasterVolumeLevel(out float levelDb);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    [PreserveSig]
    int SetMasterVolumeLevel(float levelDb, nint eventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, nint eventContext);

    // BOOL in native; int avoids source-generator bool marshalling restrictions.
    [PreserveSig]
    int GetMute(out int mute);

    [PreserveSig]
    int SetMute(int mute, nint eventContext);
}
