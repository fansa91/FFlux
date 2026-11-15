using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Services;

namespace Screenbox.Core.Tests.Services;

public class StreamUrlDetectorTests
{
    private readonly StreamUrlDetector _detector = new();

    [Test]
    [Arguments("rtsp://camera.local/live", StreamFormatType.Rtsp)]
    [Arguments("rtmp://example.com/live/stream", StreamFormatType.Rtmp)]
    [Arguments("rtmps://example.com/live/stream", StreamFormatType.Rtmp)]
    [Arguments("srt://example.com:9000", StreamFormatType.Srt)]
    [Arguments("mms://example.com/video", StreamFormatType.Mms)]
    public async Task Detect_WhenProtocolScheme_ShouldReturnProtocolFormat(string url, StreamFormatType expected)
    {
        await Assert.That(_detector.Detect(url).FormatType).IsEqualTo(expected);
    }

    [Test]
    [Arguments("https://example.com/live/index.m3u8", StreamFormatType.Hls)]
    [Arguments("https://example.com/manifest.mpd", StreamFormatType.Dash)]
    [Arguments("https://example.com/movie.mp4", StreamFormatType.Video)]
    [Arguments("https://example.com/movie.MKV", StreamFormatType.Video)]
    [Arguments("https://example.com/clip.webm", StreamFormatType.Video)]
    [Arguments("https://example.com/song.mp3", StreamFormatType.Audio)]
    [Arguments("https://example.com/album/track.flac", StreamFormatType.Audio)]
    [Arguments("https://example.com/list.m3u", StreamFormatType.Playlist)]
    public async Task Detect_WhenKnownExtension_ShouldReturnMatchingFormat(string url, StreamFormatType expected)
    {
        await Assert.That(_detector.Detect(url).FormatType).IsEqualTo(expected);
    }

    [Test]
    public async Task Detect_WhenQueryStringFollowsExtension_ShouldStillUseExtension()
    {
        StreamDetectionResult result = _detector.Detect("https://cdn.example.com/live/ch1.m3u8?token=abc&expires=1");
        await Assert.That(result.FormatType).IsEqualTo(StreamFormatType.Hls);
    }

    [Test]
    public async Task Detect_WhenNoExtension_ShouldStayUnknown()
    {
        await Assert.That(_detector.Detect("https://example.com/watch/12345").FormatType)
            .IsEqualTo(StreamFormatType.Unknown);
    }

    [Test]
    public async Task Detect_WhenM3u8_ShouldFlagAsPlaylistCandidate()
    {
        await Assert.That(_detector.Detect("https://example.com/live.m3u8").IsPlaylist).IsTrue();
    }

    [Test]
    public async Task SuggestName_WhenFileName_ShouldDropExtension()
    {
        await Assert.That(_detector.SuggestName("https://example.com/live/index.m3u8")).IsEqualTo("index");
    }

    [Test]
    public async Task SuggestName_WhenNoPath_ShouldUseHost()
    {
        await Assert.That(_detector.SuggestName("https://media.example.com")).IsEqualTo("media.example.com");
    }

    [Test]
    public async Task SuggestName_WhenTrailingSlash_ShouldSkipEmptySegment()
    {
        await Assert.That(_detector.SuggestName("https://example.com/live/")).IsEqualTo("live");
    }

    [Test]
    public async Task SuggestName_WhenPercentEncoded_ShouldDecode()
    {
        await Assert.That(_detector.SuggestName("https://example.com/%E7%9B%B4%E6%92%AD/index.m3u8"))
            .IsEqualTo("index");
    }

    [Test]
    public async Task ToBadgeText_WhenVideo_ShouldShowMp4()
    {
        await Assert.That(StreamFormatType.Video.ToBadgeText()).IsEqualTo("MP4");
        await Assert.That(StreamFormatType.Audio.ToBadgeText()).IsEqualTo("AUDIO");
        await Assert.That(StreamFormatType.Hls.ToBadgeText()).IsEqualTo("HLS");
        await Assert.That(StreamFormatType.Rtsp.ToBadgeText()).IsEqualTo("RTSP");
        await Assert.That(StreamFormatType.Unknown.ToBadgeText()).IsEqualTo("URL");
    }

    [Test]
    public async Task TryNormalize_WhenSchemeMissing_ShouldAssumeHttp()
    {
        bool ok = StreamUrlDetector.TryNormalize("example.com/live.m3u8", out Uri? uri, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(uri!.Scheme).IsEqualTo("http");
        await Assert.That(uri.AbsoluteUri).IsEqualTo("http://example.com/live.m3u8");
    }

    [Test]
    public async Task TryNormalize_WhenWindowsPath_ShouldReject()
    {
        await Assert.That(StreamUrlDetector.TryNormalize(@"C:\videos\clip.mkv", out _, out _)).IsFalse();
    }

    [Test]
    [Arguments(@"D:/media/clip.mkv")]
    [Arguments(@"\\server\share\clip.mkv")]
    [Arguments("file:///C:/videos/clip.mkv")]
    public async Task TryNormalize_WhenLocalPathShape_ShouldReject(string path)
    {
        await Assert.That(StreamUrlDetector.TryNormalize(path, out _, out _)).IsFalse();
    }

    [Test]
    public async Task TryNormalize_WhenProtocolRelative_ShouldAssumeHttp()
    {
        bool ok = StreamUrlDetector.TryNormalize("//example.com/live.m3u8", out Uri? uri, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(uri!.AbsoluteUri).IsEqualTo("http://example.com/live.m3u8");
    }

    [Test]
    public async Task TryNormalize_WhenEmpty_ShouldReject()
    {
        await Assert.That(StreamUrlDetector.TryNormalize("   ", out _, out _)).IsFalse();
    }

    [Test]
    public async Task ParseExtendedM3u_WhenPlainList_ShouldReturnEveryChannel()
    {
        const string content = """
            #EXTM3U
            #EXTINF:-1,Channel One
            http://example.com/one.m3u8
            #EXTINF:-1,Channel Two
            http://example.com/two.m3u8
            """;

        IReadOnlyList<StreamPlaylistEntry> entries =
            StreamUrlDetector.ParseExtendedM3u(content, new Uri("http://example.com/list.m3u"));

        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries[0].Name).IsEqualTo("Channel One");
        await Assert.That(entries[1].Url).IsEqualTo("http://example.com/two.m3u8");
    }

    [Test]
    public async Task ParseExtendedM3u_WhenRelativeUrls_ShouldResolveAgainstPlaylist()
    {
        const string content = """
            #EXTINF:-1,Sports
            streams/sports.m3u8
            """;

        IReadOnlyList<StreamPlaylistEntry> entries =
            StreamUrlDetector.ParseExtendedM3u(content, new Uri("http://example.com/lists/main.m3u"));

        await Assert.That(entries[0].Url).IsEqualTo("http://example.com/lists/streams/sports.m3u8");
    }

    [Test]
    public async Task ParseExtendedM3u_WhenHlsManifest_ShouldReturnNothing()
    {
        // #EXT-X- tags mean the #EXTINF entries are segments of one stream, not channels.
        const string content = """
            #EXTM3U
            #EXT-X-TARGETDURATION:6
            #EXTINF:6.0,
            segment0.ts
            #EXTINF:6.0,
            segment1.ts
            """;

        IReadOnlyList<StreamPlaylistEntry> entries =
            StreamUrlDetector.ParseExtendedM3u(content, new Uri("http://example.com/live/index.m3u8"));

        await Assert.That(entries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ParseExtendedM3u_WhenNotAPlaylist_ShouldReturnNothing()
    {
        await Assert.That(StreamUrlDetector.ParseExtendedM3u("<html></html>", new Uri("http://example.com/a.m3u")).Count)
            .IsEqualTo(0);
    }
}
