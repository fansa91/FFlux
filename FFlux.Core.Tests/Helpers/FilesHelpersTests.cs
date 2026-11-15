using Screenbox.Core.Helpers;

namespace Screenbox.Core.Tests.Helpers;

public class FilesHelpersTests
{
    [Test]
    public async Task NormalizePath_WhenAlreadyBackslashRooted_ShouldReturnSamePath()
    {
        const string path = @"C:\videos\clip.mkv";
        await Assert.That(FilesHelpers.NormalizePath(path)).IsEqualTo(path);
    }

    [Test]
    public async Task NormalizePath_WhenForwardSlashes_ShouldConvertToBackslashes()
    {
        string normalized = FilesHelpers.NormalizePath("C:/videos/sub/clip.mkv");
        await Assert.That(normalized).IsEqualTo(@"C:\videos\sub\clip.mkv");
    }

    [Test]
    public async Task NormalizePath_WhenMixedSeparators_ShouldConvertToBackslashes()
    {
        string normalized = FilesHelpers.NormalizePath(@"C:/videos\sub/clip.mkv");
        await Assert.That(normalized).IsEqualTo(@"C:\videos\sub\clip.mkv");
    }

    [Test]
    public async Task NormalizePath_WhenExtendedLengthPrefix_ShouldStripIt()
    {
        string normalized = FilesHelpers.NormalizePath(@"\\?\C:\videos\clip.mkv");
        await Assert.That(normalized).IsEqualTo(@"C:\videos\clip.mkv");
    }

    [Test]
    public async Task NormalizePath_WhenExtendedUncPrefix_ShouldStripIt()
    {
        string normalized = FilesHelpers.NormalizePath(@"\\?\UNC\server\share\clip.mkv");
        await Assert.That(normalized).IsEqualTo(@"\\server\share\clip.mkv");
    }

    [Test]
    public async Task NormalizePath_WhenRelative_ShouldReturnRootedPath()
    {
        string normalized = FilesHelpers.NormalizePath("videos/clip.mkv");
        await Assert.That(Path.IsPathRooted(normalized)).IsTrue();
        await Assert.That(normalized.EndsWith(@"videos\clip.mkv", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task NormalizePath_WhenEmpty_ShouldReturnInput()
    {
        await Assert.That(FilesHelpers.NormalizePath(string.Empty)).IsEqualTo(string.Empty);
        await Assert.That(FilesHelpers.NormalizePath("   ")).IsEqualTo("   ");
    }

    [Test]
    public async Task NormalizePath_WhenDriveLetterIsLowercase_ShouldPreserveDriveLetter()
    {
        string normalized = FilesHelpers.NormalizePath("c:/videos/clip.mkv");
        await Assert.That(normalized).IsEqualTo(@"c:\videos\clip.mkv");
    }

    [Test]
    public async Task NormalizePath_WhenForwardSlashPathExists_ShouldBeResolvableByStorageApis()
    {
        // Regression test for the real defect: Windows.Storage path APIs answer
        // ERROR_BAD_PATHNAME (0x800700A1) for any path containing a forward slash, even when
        // the file is right there. Normalisation is what makes these call sites work.
        string path = Path.Combine(Path.GetTempPath(), "screenbox-normalize-test.mkv");
        await File.WriteAllBytesAsync(path, [0, 1, 2, 3]);

        try
        {
            string forwardSlashPath = path.Replace('\\', '/');
            await Assert.That(forwardSlashPath).Contains("/");
            await Assert.That(FilesHelpers.NormalizePath(forwardSlashPath)).IsEqualTo(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
