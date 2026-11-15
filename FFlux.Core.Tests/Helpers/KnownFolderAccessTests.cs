using System;
using System.Threading.Tasks;
using Screenbox.Core.Helpers;
using Windows.Storage;

namespace Screenbox.Core.Tests.Helpers;

public class KnownFolderAccessTests
{
    /// <summary>
    /// The bug this guards against: an unpackaged process has no manifest capability, so
    /// <c>KnownFolders.RequestAccessAsync</c> answers DeniedByUser and the music library was
    /// skipped entirely - the whole library came up empty with no error.
    /// </summary>
    [Test]
    public async Task HasAccessAsync_WithoutPackageIdentity_ShouldAllowTheMusicLibrary()
    {
        if (SystemInformation.HasPackageIdentity)
        {
            // Packaged runs get their answer from the manifest capability instead.
            return;
        }

        bool hasAccess = await KnownFolderAccess.HasAccessAsync(KnownFolderId.MusicLibrary);

        await Assert.That(hasAccess).IsTrue()
            .Because("an unpackaged process reads known folders through the file system, not a capability.");
    }

    [Test]
    public async Task HasAccessAsync_WithoutPackageIdentity_ShouldAllowTheVideosLibrary()
    {
        if (SystemInformation.HasPackageIdentity) return;

        bool hasAccess = await KnownFolderAccess.HasAccessAsync(KnownFolderId.VideosLibrary);

        await Assert.That(hasAccess).IsTrue();
    }

    /// <summary>
    /// The access check must never throw. A throw here would abort
    /// <c>LibraryCoordinator.EnsureWatchingAsync</c> before the query is created.
    /// </summary>
    [Test]
    public async Task HasAccessAsync_ShouldNotThrow()
    {
        if (SystemInformation.HasPackageIdentity) return;

        foreach (KnownFolderId folder in Enum.GetValues<KnownFolderId>())
        {
            bool hasAccess = await KnownFolderAccess.HasAccessAsync(folder);
            await Assert.That(hasAccess).IsTrue().Because($"{folder} should be reported as allowed.");
        }
    }
}
