using System;
using System.Threading.Tasks;
using Windows.Storage;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Answers whether this process may read one of the Windows known folders.
/// </summary>
/// <remarks>
/// <c>KnownFolders.RequestAccessAsync</c> is gated on package identity. An unpackaged process -
/// which is what the plain <c>FFlux.exe</c> build is - has no app manifest, so there is no
/// capability to grant and the call answers <see cref="KnownFoldersAccessStatus.DeniedByUser"/>
/// every single time, even though the process can read the folder through the normal file system.
/// Treating that answer as "no access" is what made the music and videos libraries come up empty,
/// so the request is skipped entirely when there is no identity.
/// </remarks>
public static class KnownFolderAccess
{
    /// <summary>
    /// Whether the app may enumerate <paramref name="folderId"/>.
    /// </summary>
    public static async Task<bool> HasAccessAsync(KnownFolderId folderId)
    {
        if (!SystemInformation.IsKnownFolderRequestAccessSupported || !SystemInformation.HasPackageIdentity)
        {
            // Nothing to ask for: either the API does not exist on this OS, or there is no
            // manifest capability to grant. A full-trust desktop process is bound by file system
            // permissions either way, so access is assumed and any real failure surfaces when the
            // query runs.
            return true;
        }

        KnownFoldersAccessStatus status = await KnownFolders.RequestAccessAsync(folderId);
        return status is KnownFoldersAccessStatus.Allowed or KnownFoldersAccessStatus.AllowedPerAppFolder;
    }
}
