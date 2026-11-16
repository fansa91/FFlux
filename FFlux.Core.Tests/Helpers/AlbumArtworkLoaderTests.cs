using System.Collections.Generic;
using System.Linq;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Tests.Helpers;

public class AlbumArtworkLoaderTests
{
    /// <summary>
    /// The bug this guards against: album art used to come only from the shell thumbnail provider,
    /// which answers audio files with the generic file-type icon when the process has no package
    /// identity, so every cover was blank. A cover image next to the tracks replaces it, and the
    /// priority order decides which file wins when a folder ships several.
    /// </summary>
    [Test]
    public async Task OrderCoverCandidates_ShouldRankCoverStemBeforeFolderStem()
    {
        IReadOnlyList<string> ordered = AlbumArtworkLoader.OrderCoverCandidates(
            new[] { "folder.jpg", "cover.jpg", "01 track.mp3" });

        await Assert.That(ordered[0]).IsEqualTo("cover.jpg");
        await Assert.That(ordered[1]).IsEqualTo("folder.jpg");
    }

    [Test]
    public async Task OrderCoverCandidates_ShouldRankJpegBeforePngWithinTheSameStem()
    {
        IReadOnlyList<string> ordered = AlbumArtworkLoader.OrderCoverCandidates(
            new[] { "cover.png", "cover.jpg" });

        await Assert.That(ordered[0]).IsEqualTo("cover.jpg");
        await Assert.That(ordered[1]).IsEqualTo("cover.png");
    }

    [Test]
    public async Task OrderCoverCandidates_ShouldIgnoreEverythingThatIsNotACoverImage()
    {
        IReadOnlyList<string> ordered = AlbumArtworkLoader.OrderCoverCandidates(
            new[] { "01 track.mp3", "cover.txt", "album.nfo", "folder.jpg" });

        await Assert.That(ordered.Count).IsEqualTo(1);
        await Assert.That(ordered[0]).IsEqualTo("folder.jpg");
    }

    [Test]
    public async Task OrderCoverCandidates_ShouldMatchNamesCaseInsensitively()
    {
        IReadOnlyList<string> ordered = AlbumArtworkLoader.OrderCoverCandidates(new[] { "Cover.JPG" });

        await Assert.That(ordered.Single()).IsEqualTo("Cover.JPG");
    }

    [Test]
    public async Task OrderCoverCandidates_ShouldReturnNothingForAFolderOfTracks()
    {
        IReadOnlyList<string> ordered = AlbumArtworkLoader.OrderCoverCandidates(
            new[] { "01 - The Fate of Ophelia.mp3", "02 - Elizabeth Taylor.mp3" });

        await Assert.That(ordered.Count).IsEqualTo(0);
    }

    /// <summary>
    /// The entry point rejects a null file outright: callers always have a file by the time they
    /// ask for artwork, so a null here means a programming error worth surfacing immediately.
    /// </summary>
    [Test]
    public async Task LoadAsync_NullFile_ShouldThrowArgumentNullException()
    {
        bool threw = false;
        try
        {
            await AlbumArtworkLoader.LoadAsync(null!);
        }
        catch (ArgumentNullException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }
}
