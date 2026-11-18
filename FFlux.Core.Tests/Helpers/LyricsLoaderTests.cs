using System.Linq;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Tests.Helpers;

public class LyricsLoaderTests
{
    [Test]
    public async Task OrderCandidates_ShouldPreferExactStemThenSharedNames()
    {
        string[] files = ["album cover.jpg", "song.LRC", "lyrics.lrc", "other.lrc", "song.flac"];

        var ranked = LyricsLoader.OrderCandidates(files, "song.flac");

        await Assert.That(ranked.Count).IsEqualTo(3);
        await Assert.That(ranked[0]).IsEqualTo("song.LRC");
        await Assert.That(ranked[1]).IsEqualTo("lyrics.lrc");
        await Assert.That(ranked[2]).IsEqualTo("other.lrc");
    }

    [Test]
    public async Task OrderCandidates_WithoutAnyLrc_ShouldReturnEmpty()
    {
        var ranked = LyricsLoader.OrderCandidates(["song.flac", "cover.jpg"], "song.flac");

        await Assert.That(ranked.Count).IsEqualTo(0);
    }

    [Test]
    public async Task OrderCandidates_EmptyFileName_ShouldReturnEmpty()
    {
        await Assert.That(LyricsLoader.OrderCandidates(["song.lrc"], "").Count).IsEqualTo(0);
    }
}
