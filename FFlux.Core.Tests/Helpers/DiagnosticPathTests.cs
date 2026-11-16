using System;
using System.IO;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Tests.Helpers;

public class DiagnosticPathTests
{
    /// <summary>
    /// The bug this guards against: automation and service shells can strip LOCALAPPDATA entirely,
    /// so Environment.GetFolderPath(LocalApplicationData) returns empty and Path.Combine would
    /// silently build a CWD-relative log path. Diagnostics would then land wherever the exe was
    /// started from (observed: next to the binary) instead of the real app-data folder, and every
    /// "check the log" instruction silently reads the wrong place.
    /// </summary>
    [Test]
    public async Task ResolveLogDirectory_WithEmptyLocalAppData_ShouldFallBackToAppDirectory()
    {
        string resolved = PlaybackTrace.ResolveLogDirectory(string.Empty, "FFlux");

        await Assert.That(Path.IsPathRooted(resolved)).IsTrue();
        await Assert.That(resolved.EndsWith("FFlux", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ResolveLogDirectory_WithRealLocalAppData_ShouldUseIt()
    {
        string resolved = PlaybackTrace.ResolveLogDirectory(@"C:\Users\test\AppData\Local", "FFlux");

        await Assert.That(resolved).IsEqualTo(@"C:\Users\test\AppData\Local\FFlux");
    }

    [Test]
    public async Task ResolveLogDirectory_WithCustomFolderName_ShouldAppendIt()
    {
        string resolved = PlaybackTrace.ResolveLogDirectory(@"C:\data", "Other");

        await Assert.That(resolved).IsEqualTo(@"C:\data\Other");
    }
}
