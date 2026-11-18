using System;
using System.Linq;
using Screenbox.Core.Models;

namespace Screenbox.Core.Tests.Models;

public class LrcParserTests
{
    [Test]
    public async Task Parse_TypicalLrc_ShouldReturnTimedLines()
    {
        var lines = LrcParser.Parse("[00:01.50]First line\n[00:03.25]Second line");

        await Assert.That(lines.Count).IsEqualTo(2);
        await Assert.That(lines[0].Time).IsEqualTo(TimeSpan.FromMilliseconds(1500));
        await Assert.That(lines[0].Text).IsEqualTo("First line");
        await Assert.That(lines[1].Time).IsEqualTo(TimeSpan.FromMilliseconds(3250));
    }

    [Test]
    public async Task Parse_MetadataAndOffset_ShouldIgnoreTagsAndShiftLines()
    {
        const string lrc = """
            [ti:Song]
            [ar:Artist]
            [offset:500]
            [00:02.00]Hello
            """;

        var lines = LrcParser.Parse(lrc);

        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0].Time).IsEqualTo(TimeSpan.FromMilliseconds(2500));
        await Assert.That(lines[0].Text).IsEqualTo("Hello");
    }

    [Test]
    public async Task Parse_MultipleTimestamps_ShouldEmitOneLinePerTimestamp()
    {
        var lines = LrcParser.Parse("[00:10.00][00:20.00]Chorus");

        await Assert.That(lines.Count).IsEqualTo(2);
        await Assert.That(lines[0].Text).IsEqualTo("Chorus");
        await Assert.That(lines[1].Time).IsEqualTo(TimeSpan.FromSeconds(20));
    }

    [Test]
    public async Task Parse_OutOfOrderLines_ShouldSortByTime()
    {
        var lines = LrcParser.Parse("[00:05.00]Late\n[00:01.00]Early");

        await Assert.That(lines[0].Text).IsEqualTo("Early");
        await Assert.That(lines[1].Text).IsEqualTo("Late");
    }

    [Test]
    public async Task Parse_MalformedAndEmptyInput_ShouldReturnEmpty()
    {
        await Assert.That(LrcParser.Parse(null).Count).IsEqualTo(0);
        await Assert.That(LrcParser.Parse("   ").Count).IsEqualTo(0);
        await Assert.That(LrcParser.Parse("[not a timestamp]text\n[00:01.00]").Count).IsEqualTo(0);
    }

    [Test]
    public async Task FindLineIndex_ShouldReturnLastLineAtOrBeforePosition()
    {
        var lines = LrcParser.Parse("[00:01.00]A\n[00:05.00]B\n[00:10.00]C");

        await Assert.That(LrcParser.FindLineIndex(lines, TimeSpan.Zero)).IsEqualTo(-1);
        await Assert.That(LrcParser.FindLineIndex(lines, TimeSpan.FromSeconds(1))).IsEqualTo(0);
        await Assert.That(LrcParser.FindLineIndex(lines, TimeSpan.FromSeconds(7))).IsEqualTo(1);
        await Assert.That(LrcParser.FindLineIndex(lines, TimeSpan.FromSeconds(60))).IsEqualTo(2);
    }
}
