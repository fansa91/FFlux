using System;
using System.Runtime.Versioning;
using Windows.ApplicationModel;
using Windows.Foundation.Metadata;
using Windows.System.Profile;

namespace Screenbox.Core.Helpers;

public static class SystemInformation
{
    public static readonly string DeviceFamily = AnalyticsInfo.VersionInfo.DeviceFamily;

    /// <summary>
    /// Whether this process runs with package identity (installed as an MSIX).
    /// </summary>
    /// <remarks>
    /// Identity decides what the known-folder APIs can do: the music, videos and pictures
    /// libraries exist for packaged apps only, and an unpackaged process has no manifest in which
    /// to declare those capabilities. <c>Package.Current</c> throws when there is no identity, so
    /// probing it is the documented way to tell the two apart.
    /// </remarks>
    public static bool HasPackageIdentity => _hasPackageIdentity.Value;

    private static readonly Lazy<bool> _hasPackageIdentity = new(() =>
    {
        try
        {
            _ = Package.Current;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    });

    [SupportedOSPlatformGuard("windows10.0.19041")]
    #pragma warning disable CA1416
    public static bool IsKnownFolderRequestAccessSupported =>
        ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 10);
    #pragma warning restore CA1416

    public static bool IsDesktop => DeviceFamily == "Windows.Desktop";

    public static bool IsXbox => DeviceFamily == "Windows.Xbox";
}
