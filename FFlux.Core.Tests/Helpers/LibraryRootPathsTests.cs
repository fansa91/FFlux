using System.Collections.Generic;
using System.Linq;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Tests.Helpers;

public class LibraryRootPathsTests
{
    /// <summary>
    /// The bug this guards against: the scan used to run against a single folder picked with
    /// <c>Folders.FirstOrDefault()</c>, so everything the user kept in a second library folder was
    /// invisible. Every folder now takes part, and the merge has to keep them apart without
    /// duplicating anything.
    /// </summary>
    [Test]
    public async Task Merge_ShouldKeepEveryLibraryFolder()
    {
        IReadOnlyList<string> merged = LibraryRootPaths.Merge(
            new[] { @"C:\Users\me\Music", @"D:\Archive\Music" },
            new[] { @"E:\Incoming" });

        await Assert.That(merged.Count).IsEqualTo(3);
        await Assert.That(merged[0]).IsEqualTo(@"C:\Users\me\Music");
        await Assert.That(merged[1]).IsEqualTo(@"D:\Archive\Music");
        await Assert.That(merged[2]).IsEqualTo(@"E:\Incoming");
    }

    [Test]
    public async Task Merge_ShouldDropAFolderAddedTwice()
    {
        IReadOnlyList<string> merged = LibraryRootPaths.Merge(
            new[] { @"C:\Users\me\Music" },
            new[] { @"c:\users\me\music" });

        await Assert.That(merged.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Merge_ShouldDropAnExtraFolderThatLivesInsideALibraryFolder()
    {
        // Scanning the parent already returns the subfolder, so keeping both would list its files twice.
        IReadOnlyList<string> merged = LibraryRootPaths.Merge(
            new[] { @"C:\Users\me\Music" },
            new[] { @"C:\Users\me\Music\Rock" });

        await Assert.That(merged.Single()).IsEqualTo(@"C:\Users\me\Music");
    }

    [Test]
    public async Task Merge_ShouldKeepAFolderThatOnlySharesAPrefix()
    {
        IReadOnlyList<string> merged = LibraryRootPaths.Merge(
            new[] { @"C:\Music" },
            new[] { @"C:\MusicLive" });

        await Assert.That(merged.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Merge_ShouldIgnoreTrailingSeparatorsAndBlankEntries()
    {
        IReadOnlyList<string> merged = LibraryRootPaths.Merge(
            new[] { @"C:\Users\me\Music\", null, "   " },
            new[] { @"C:\Users\me\Music" });

        await Assert.That(merged.Single()).IsEqualTo(@"C:\Users\me\Music");
    }

    [Test]
    public async Task Merge_ShouldKeepADriveRootIntact()
    {
        IReadOnlyList<string> merged = LibraryRootPaths.Merge(new[] { @"D:\" }, []);

        await Assert.That(merged.Single()).IsEqualTo(@"D:\");
    }

    [Test]
    public async Task Covers_ShouldAnswerTrueForTheSameFolderAndItsChildren()
    {
        string[] roots = [@"C:\Users\me\Music"];

        await Assert.That(LibraryRootPaths.Covers(roots, @"c:\users\me\music")).IsTrue();
        await Assert.That(LibraryRootPaths.Covers(roots, @"C:\Users\me\Music\Live")).IsTrue();
        await Assert.That(LibraryRootPaths.Covers(roots, @"C:\Users\me\MusicLive")).IsFalse();
        await Assert.That(LibraryRootPaths.Covers(roots, null)).IsFalse();
    }

    [Test]
    public async Task Covers_ShouldTreatADriveRootAsItsOwnParent()
    {
        string[] roots = [@"D:\"];

        await Assert.That(LibraryRootPaths.Covers(roots, @"D:\Music")).IsTrue();
        await Assert.That(LibraryRootPaths.Covers(roots, @"C:\Music")).IsFalse();
    }
}
