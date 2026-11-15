using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using FFmpegInteropX;
using Microsoft.UI.Xaml.Media.Imaging;
using Screenbox.Core.Helpers;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;
using Windows.Storage.Streams;

namespace Screenbox.Helpers;

/// <summary>
/// Diagnostic-only (<c>FFLUX_COVER_PROBE=1</c>): reports what every album art source actually
/// produces for the first songs of the music library.
/// </summary>
/// <remarks>
/// Album art has three possible providers - the shell thumbnail cache, a cover image next to the
/// file, and the artwork embedded in the container - and an unpackaged process can silently lose
/// any of them. The probe runs each one against a real library file and logs the raw outcome, so
/// the blank cover has a cause instead of a guess.
/// </remarks>
internal static class CoverProbe
{
    private static void Log(string message) => StartupErrorLog.Write("Cover probe: " + message);

    public static async Task RunAsync()
    {
        // Let the normal library fetch finish first; the point is to inspect files the UI sees.
        await Task.Delay(5000);

        try
        {
            ILibraryService libraryService = Ioc.Default.GetRequiredService<ILibraryService>();

            StorageLibrary musicLibrary = await libraryService.InitializeMusicLibraryAsync();
            if (musicLibrary.Folders.FirstOrDefault() is { } musicRoot)
            {
                await InspectRootAsync(musicRoot, FilesHelpers.SupportedAudioFormats.ToArray(), InspectAsync);
            }
            else
            {
                Log("no music library root");
            }

            StorageLibrary videosLibrary = await libraryService.InitializeVideosLibraryAsync();
            if (videosLibrary.Folders.FirstOrDefault() is { } videosRoot)
            {
                await InspectRootAsync(videosRoot, FilesHelpers.SupportedVideoFormats.ToArray(), InspectShellAsync);
            }
            else
            {
                Log("no videos library root");
            }
        }
        catch (Exception ex)
        {
            Log("failed: " + ex);
        }
    }

    private static async Task InspectRootAsync(
        StorageFolder root,
        string[] formats,
        Func<StorageFile, Task> inspect)
    {
        QueryOptions options = new(CommonFileQuery.OrderByTitle, formats)
        {
            IndexerOption = IndexerOption.DoNotUseIndexer
        };
        StorageFileQueryResult query = root.CreateFileQueryWithOptions(options);
        IReadOnlyList<StorageFile> files = await query.GetFilesAsync(0, 2);
        Log($"root='{root.Path}' sample={files.Count}");
        foreach (StorageFile file in files)
        {
            await inspect(file);
        }
    }

    /// <summary>
    /// Reports the shell thumbnail outcome only. Used for videos, where the album art sources do
    /// not apply and the shell is the whole story.
    /// </summary>
    private static async Task InspectShellAsync(StorageFile file)
    {
        Log($"file='{file.Name}' available={file.IsAvailable}");
        await InspectShellThumbnailsAsync(file);
    }

    private static async Task InspectShellThumbnailsAsync(StorageFile file)
    {
        foreach (ThumbnailMode mode in new[]
                 {
                     ThumbnailMode.SingleItem, ThumbnailMode.VideosView,
                     ThumbnailMode.MusicView, ThumbnailMode.PicturesView
                 })
        {
            try
            {
                StorageItemThumbnail? thumbnail =
                    await file.GetThumbnailAsync(mode, 1280, ThumbnailOptions.UseCurrentScale);
                if (thumbnail is null)
                {
                    Log($"  {mode}: null thumbnail");
                    continue;
                }

                using (thumbnail)
                {
                    Log($"  {mode}: type={thumbnail.Type} contentType='{thumbnail.ContentType}' " +
                        $"size={thumbnail.OriginalWidth}x{thumbnail.OriginalHeight} " +
                        $"returned={thumbnail.ReturnedSmallerCachedSize}");

                    if (thumbnail.Type == ThumbnailType.Image)
                    {
                        try
                        {
                            BitmapImage image = new()
                            {
                                DecodePixelType = DecodePixelType.Logical,
                                DecodePixelHeight = 300
                            };
                            await image.SetSourceAsync(thumbnail);
                            Log($"  {mode}: decoded {image.PixelWidth}x{image.PixelHeight}");
                        }
                        catch (Exception ex)
                        {
                            Log($"  {mode}: decode FAILED {ex.GetType().Name} hr=0x{ex.HResult:X8}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"  {mode}: FAILED {ex.GetType().Name} hr=0x{ex.HResult:X8}: {ex.Message}");
            }
        }
    }

    private static async Task InspectAsync(StorageFile file)
    {
        Log($"file='{file.Name}' available={file.IsAvailable}");

        await InspectShellThumbnailsAsync(file);

        try
        {
            StorageFolder? parent = await file.GetParentAsync();
            if (parent is null)
            {
                Log("  sibling cover: no parent folder");
            }
            else
            {
                foreach (string name in new[] { "cover.jpg", "folder.jpg", "front.jpg", "album.jpg", "cover.png", "folder.png" })
                {
                    IStorageItem? item = await parent.TryGetItemAsync(name);
                    if (item is not null) Log($"  sibling cover: {name} found");
                }
            }
        }
        catch (Exception ex)
        {
            Log("  sibling cover FAILED: " + ex);
        }

        try
        {
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            MediaSourceConfig config = FFmpegMediaSourceConfigFactory.Create();
            FFmpegMediaSource source = await FFmpegMediaSource.CreateFromStreamAsync(stream, config);
            Log($"  ffmpeg: hasThumbnail={source.HasThumbnail}");
            if (source.HasThumbnail)
            {
                MediaThumbnailData thumbnail = source.ExtractThumbnail();
                try
                {
                    Log($"  ffmpeg: coverage ext='{thumbnail.Extension}' bytes={thumbnail.Buffer?.Length ?? 0}");
                }
                finally
                {
                    thumbnail.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Log("  ffmpeg FAILED: " + ex);
        }

        try
        {
            byte[]? artwork = await AlbumArtworkLoader.LoadAsync(file);
            Log($"  loader: bytes={artwork?.Length ?? 0}");
            if (artwork is { Length: > 0 })
            {
                // Same decode path the album grid uses; a size here means the cover reaches the UI.
                using InMemoryRandomAccessStream stream = new();
                using (DataWriter writer = new(stream))
                {
                    writer.WriteBytes(artwork);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }

                stream.Seek(0);
                BitmapImage image = new() { DecodePixelHeight = 300 };
                await image.SetSourceAsync(stream);
                Log($"  loader: decoded {image.PixelWidth}x{image.PixelHeight}");
            }
        }
        catch (Exception ex)
        {
            Log("  loader FAILED: " + ex);
        }
    }
}
