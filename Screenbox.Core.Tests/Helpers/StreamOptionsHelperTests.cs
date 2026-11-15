using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;

namespace Screenbox.Core.Tests.Helpers;

public class StreamOptionsHelperTests
{
    [Test]
    public async Task ToFFmpegOptions_WhenNothingSet_ShouldReturnEmpty()
    {
        string[] options = StreamOptionsHelper.ToFFmpegOptions(new StreamItem { Url = "https://e.com/a.mp4" });
        await Assert.That(options.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ToFFmpegOptions_WhenUserAgentAndReferer_ShouldEmitBoth()
    {
        var item = new StreamItem
        {
            Url = "https://e.com/a.m3u8",
            FormatType = StreamFormatType.Hls,
            UserAgent = " Mozilla/5.0 ",
            Referer = " https://e.com/ ",
        };

        string[] options = StreamOptionsHelper.ToFFmpegOptions(item);

        await Assert.That(options.Length).IsEqualTo(2);
        await Assert.That(options[0]).IsEqualTo("user_agent=Mozilla/5.0");
        await Assert.That(options[1]).IsEqualTo("referer=https://e.com/");
    }

    [Test]
    public async Task ToFFmpegOptions_WhenHeaders_ShouldJoinWithCrLf()
    {
        var item = new StreamItem
        {
            Url = "https://e.com/a.mp4",
            Headers = "Cookie: a=b\nX-Token: 123\n",
        };

        string[] options = StreamOptionsHelper.ToFFmpegOptions(item);

        await Assert.That(options.Length).IsEqualTo(1);
        await Assert.That(options[0]).IsEqualTo("headers=Cookie: a=b\r\nX-Token: 123");
    }

    [Test]
    public async Task NormalizeHeaders_WhenLineHasNoColon_ShouldDropIt()
    {
        string headers = StreamOptionsHelper.NormalizeHeaders("bad line\nGood: value");
        await Assert.That(headers).IsEqualTo("Good: value");
    }

    [Test]
    public async Task NormalizeHeaders_WhenEmptyValue_ShouldDropPair()
    {
        await Assert.That(StreamOptionsHelper.NormalizeHeaders("Empty:")).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task OpenUrlRequest_WhenOnlyUrl_ShouldNotReportNetworkOptions()
    {
        var request = new OpenUrlRequest("https://e.com/a.mp4");

        await Assert.That(request.HasNetworkOptions).IsFalse();
        await Assert.That(request.ToFFmpegOptions().Length).IsEqualTo(0);
    }

    [Test]
    public async Task OpenUrlRequest_WhenRefererSet_ShouldExposeIt()
    {
        var request = new OpenUrlRequest("https://e.com/a.m3u8", Referer: "https://e.com/");

        await Assert.That(request.HasNetworkOptions).IsTrue();
        await Assert.That(request.ToFFmpegOptions()[0]).IsEqualTo("referer=https://e.com/");
    }
}
