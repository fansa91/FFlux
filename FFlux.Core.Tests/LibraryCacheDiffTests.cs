using System;
using System.Collections.Generic;
using System.Linq;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;

namespace Screenbox.Core.Tests;

public class LibraryCacheDiffTests
{
    [Test]
    public async Task HaveSamePaths_IgnoresOrderAndCasing()
    {
        string[] current = [@"C:\Music\B.mp3", @"C:\Music\A.mp3"];
        string[] cached = [@"c:\music\a.mp3", @"C:\Music\b.mp3"];

        await Assert.That(LibraryCacheDiff.HaveSamePaths(current, cached)).IsTrue();
    }

    [Test]
    public async Task HaveSamePaths_DetectsAdditionsAndRemovals()
    {
        await Assert.That(LibraryCacheDiff.HaveSamePaths(
            ["a", "b"], ["a"])).IsFalse();
        await Assert.That(LibraryCacheDiff.HaveSamePaths(
            ["a"], ["a", "b"])).IsFalse();
        await Assert.That(LibraryCacheDiff.HaveSamePaths(
            Array.Empty<string>(), Array.Empty<string>())).IsTrue();
    }

    [Test]
    public async Task HaveStampsChanged_NoBaseline_RequiresAFullParse()
    {
        Dictionary<string, FileStampDto> current = new()
        {
            ["a"] = new FileStampDto { Path = "a", Size = 10, ModifiedTicks = 1 }
        };

        await Assert.That(LibraryCacheDiff.HaveStampsChanged(null, current)).IsTrue();
        await Assert.That(LibraryCacheDiff.HaveStampsChanged(
            new Dictionary<string, FileStampDto>(), current)).IsTrue();
    }

    [Test]
    public async Task HaveStampsChanged_IdenticalFingerprints_IsFalse()
    {
        Dictionary<string, FileStampDto> known = new Dictionary<string, FileStampDto>
        {
            ["a"] = new() { Path = "a", Size = 10, ModifiedTicks = 1 },
            ["b"] = new() { Path = "b", Size = 20, ModifiedTicks = 2 },
        };
        IReadOnlyDictionary<string, FileStampDto> current = new Dictionary<string, FileStampDto>
        {
            ["b"] = new() { Path = "b", Size = 20, ModifiedTicks = 2 },
            ["a"] = new() { Path = "a", Size = 10, ModifiedTicks = 1 },
        };

        await Assert.That(LibraryCacheDiff.HaveStampsChanged(known, current)).IsFalse();
    }

    [Test]
    public async Task HaveStampsChanged_DetectsSizeMtimeAndPathChanges()
    {
        Dictionary<string, FileStampDto> known = new Dictionary<string, FileStampDto>
        {
            ["a"] = new() { Path = "a", Size = 10, ModifiedTicks = 1 },
            ["b"] = new() { Path = "b", Size = 20, ModifiedTicks = 2 },
        };

        IReadOnlyDictionary<string, FileStampDto> sizeChanged = new Dictionary<string, FileStampDto>(known)
        {
            ["a"] = new() { Path = "a", Size = 11, ModifiedTicks = 1 },
        };
        IReadOnlyDictionary<string, FileStampDto> timeChanged = new Dictionary<string, FileStampDto>(known)
        {
            ["b"] = new() { Path = "b", Size = 20, ModifiedTicks = 99 },
        };
        IReadOnlyDictionary<string, FileStampDto> newFile = new Dictionary<string, FileStampDto>(known)
        {
            ["c"] = new() { Path = "c", Size = 1, ModifiedTicks = 1 },
        };

        await Assert.That(LibraryCacheDiff.HaveStampsChanged(known, sizeChanged)).IsTrue();
        await Assert.That(LibraryCacheDiff.HaveStampsChanged(known, timeChanged)).IsTrue();
        await Assert.That(LibraryCacheDiff.HaveStampsChanged(known, newFile)).IsTrue();
    }

    [Test]
    public async Task SelectChangedPaths_ReturnsOnlyNewOrModifiedFiles()
    {
        Dictionary<string, FileStampDto> known = new Dictionary<string, FileStampDto>
        {
            ["unchanged"] = new() { Path = "unchanged", Size = 10, ModifiedTicks = 1 },
            ["edited"] = new() { Path = "edited", Size = 10, ModifiedTicks = 1 },
        };
        IReadOnlyDictionary<string, FileStampDto> current = new Dictionary<string, FileStampDto>
        {
            ["unchanged"] = new() { Path = "unchanged", Size = 10, ModifiedTicks = 1 },
            ["edited"] = new() { Path = "edited", Size = 12, ModifiedTicks = 1 },
            ["new"] = new() { Path = "new", Size = 1, ModifiedTicks = 2 },
        };

        IReadOnlyList<string> changed = LibraryCacheDiff.SelectChangedPaths(known, current);

        await Assert.That(changed.Count).IsEqualTo(2);
        await Assert.That(changed.Contains("edited")).IsTrue();
        await Assert.That(changed.Contains("new")).IsTrue();
        await Assert.That(changed.Contains("unchanged")).IsFalse();
    }
}
