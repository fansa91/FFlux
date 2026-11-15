using System.Threading.Tasks;
using Screenbox.Core.Enums;
using Screenbox.Core.ViewModels;
using Windows.Storage.FileProperties;

namespace Screenbox.Core.Tests.ViewModels;

public partial class MediaViewModelTests
{
    /// <summary>
    /// The bug this guards against: the video grid asked the shell for a thumbnail in
    /// <see cref="ThumbnailMode.SingleItem"/> mode, and the shell answers that request for a video
    /// with the generic file-type icon instead of a frame, so every video tile was blank.
    /// </summary>
    [Test]
    public async Task GetShellThumbnailMode_ShouldAskForAFrameForVideo()
    {
        ThumbnailMode mode = MediaViewModel.GetShellThumbnailMode(MediaPlaybackType.Video);

        await Assert.That(mode).IsEqualTo(ThumbnailMode.VideosView);
    }

    [Test]
    public async Task GetShellThumbnailMode_ShouldKeepSingleItemForMusic()
    {
        ThumbnailMode mode = MediaViewModel.GetShellThumbnailMode(MediaPlaybackType.Music);

        await Assert.That(mode).IsEqualTo(ThumbnailMode.SingleItem);
    }

    [Test]
    public async Task GetShellThumbnailMode_ShouldKeepSingleItemForUnknownMedia()
    {
        ThumbnailMode mode = MediaViewModel.GetShellThumbnailMode(MediaPlaybackType.Unknown);

        await Assert.That(mode).IsEqualTo(ThumbnailMode.SingleItem);
    }
}
