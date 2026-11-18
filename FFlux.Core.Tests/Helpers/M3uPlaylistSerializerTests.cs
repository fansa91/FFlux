using System;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Tests.Helpers;

public class M3uPlaylistSerializerTests
{
    [Test]
    public async Task Serialize_ShouldWriteHeaderExtInfAndPaths()
    {
        var entries = new[]
        {
            new M3uEntry(@"C:\Music\song.mp3", "Song", TimeSpan.FromSeconds(123.4)),
            new M3uEntry("https://example.com/radio", "Radio", TimeSpan.Zero),
        };

        string result = M3uPlaylistSerializer.Serialize(entries);

        await Assert.That(result.StartsWith("#EXTM3U")).IsTrue();
        await Assert.That(result).Contains("#EXTINF:123,Song");
        await Assert.That(result).Contains("file:///C:/Music/song.mp3");
        await Assert.That(result).Contains("#EXTINF:-1,Radio");
        await Assert.That(result).Contains("https://example.com/radio");
    }

    [Test]
    public async Task Serialize_ShouldSkipBlankAndPlaceholderPaths()
    {
        var entries = new[]
        {
            new M3uEntry("", "Empty", TimeSpan.Zero),
            new M3uEntry("about:blank", "Placeholder", TimeSpan.Zero),
            new M3uEntry(@"C:\Music\song.mp3", "Song", TimeSpan.Zero),
        };

        string result = M3uPlaylistSerializer.Serialize(entries);

        await Assert.That(result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length).IsEqualTo(3); // header + #EXTINF + path
        await Assert.That(result).DoesNotContain("Placeholder");
    }

    [Test]
    public async Task Encode_ShouldIncludeUtf8Bom()
    {
        byte[] bytes = M3uPlaylistSerializer.Encode("#EXTM3U");

        await Assert.That(bytes.Length).IsEqualTo(10); // 3 BOM + 7 "#EXTM3U"
        await Assert.That(bytes[0]).IsEqualTo((byte)0xEF);
        await Assert.That(bytes[1]).IsEqualTo((byte)0xBB);
        await Assert.That(bytes[2]).IsEqualTo((byte)0xBF);
    }
}
