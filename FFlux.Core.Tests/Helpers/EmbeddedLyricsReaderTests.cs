using System.Collections.Generic;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;

namespace Screenbox.Core.Tests.Helpers;

public class EmbeddedLyricsReaderTests
{
    private static KeyValuePair<string, string> Tag(string key, string value) => new(key, value);

    [Test]
    public async Task FindLyricsText_CommonKeys_ShouldReturnLrcText()
    {
        foreach (string key in new[] { "lyrics", "LYRICS", "unsyncedlyrics", "USLT", "©lyr", "WM/Lyrics" })
        {
            string? text = EmbeddedLyricsReader.FindLyricsText(
                new[] { Tag("title", "Song"), Tag(key, "[00:01.00]Hello") });

            await Assert.That(text).IsEqualTo("[00:01.00]Hello");
        }
    }

    [Test]
    public async Task FindLyricsText_LanguageVariant_ShouldReturnText()
    {
        var text = EmbeddedLyricsReader.FindLyricsText(
            new[] { Tag("lyrics-eng", "[00:02.00]Line") });

        await Assert.That(text).IsEqualTo("[00:02.00]Line");
    }

    [Test]
    public async Task FindLyricsText_PrefersExactLyricsKeyOverVariant()
    {
        var text = EmbeddedLyricsReader.FindLyricsText(
            new[]
            {
                Tag("lyrics-eng", "[00:05.00]Variant"),
                Tag("lyrics", "[00:01.00]Exact")
            });

        await Assert.That(text).IsEqualTo("[00:01.00]Exact");
    }

    [Test]
    public async Task FindLyricsText_JoinsMultipleValuesUnderBestKey()
    {
        var text = EmbeddedLyricsReader.FindLyricsText(
            new[]
            {
                Tag("lyrics", "[00:01.00]First"),
                Tag("lyrics", "[00:02.00]Second")
            });

        await Assert.That(text).IsEqualTo("[00:01.00]First\n[00:02.00]Second");
    }

    [Test]
    public async Task FindLyricsText_WithoutLyricTags_ShouldReturnNull()
    {
        var text = EmbeddedLyricsReader.FindLyricsText(
            new[] { Tag("title", "Song"), Tag("artist", "Artist"), Tag("comment", "no lyrics") });

        await Assert.That(text).IsNull();
    }

    [Test]
    public async Task FindLyricsText_WithEmptyValues_ShouldReturnNull()
    {
        var text = EmbeddedLyricsReader.FindLyricsText(
            new[] { Tag("lyrics", "  "), Tag("unsyncedlyrics", string.Empty) });

        await Assert.That(text).IsNull();
    }

    [Test]
    public async Task EmbeddedLrcText_ShouldParseIntoTimedLines()
    {
        const string embedded = """
            [ti:Song]
            [ar:Artist]
            [00:01.00]First line
            [00:03.00]Second line
            """;

        string text = EmbeddedLyricsReader.FindLyricsText(new[] { Tag("lyrics", embedded) })!;
        var lines = LrcParser.Parse(text);

        await Assert.That(lines.Count).IsEqualTo(2);
        await Assert.That(lines[0].Text).IsEqualTo("First line");
        await Assert.That(lines[1].Time).IsEqualTo(TimeSpan.FromSeconds(3));
    }
}
