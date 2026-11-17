using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.MediaSources;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Screenbox.Core.ViewModels;

public sealed partial class MediaViewModel : ObservableRecipient
{
    public string Location => Source.Location;

    public IMediaSource Source { get; }

    public bool IsFromLibrary { get; set; }

    public bool DetailsLoaded { get; private set; }

    public ArtistViewModel? MainArtist => Artists.FirstOrDefault();

    public IReadOnlyList<string> Options { get; }

    public DateTimeOffset DateAdded { get; set; }

    public MediaPlaybackType MediaType => MediaInfo.MediaType;

    public TimeSpan Duration => MediaInfo.MusicProperties.Duration > TimeSpan.Zero
        ? MediaInfo.MusicProperties.Duration
        : MediaInfo.VideoProperties.Duration;

    public string TrackNumberText =>
        MediaInfo.MusicProperties.TrackNumber > 0 ? MediaInfo.MusicProperties.TrackNumber.ToString() : string.Empty;    // Helper for binding

    public BitmapImage? Thumbnail
    {
        get
        {
            if (_thumbnailRef == null) return null;
            return _thumbnailRef.TryGetTarget(out BitmapImage? image) ? image : null;
        }
        set
        {
            if (_thumbnailRef == null && value == null) return;
            if ((_thumbnailRef?.TryGetTarget(out BitmapImage? image) ?? false) && image == value) return;
            SetProperty(ref _thumbnailRef, value == null ? null : new WeakReference<BitmapImage>(value));
        }
    }

    private readonly List<string> _options;

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsMediaActive { get; set; }
    [ObservableProperty] public partial bool IsAvailable { get; set; } = true;
    [ObservableProperty] public partial AlbumViewModel? Album { get; set; }
    [ObservableProperty] public partial string Caption { get; set; } = string.Empty;  // For list item subtitle
    [ObservableProperty] public partial string AltCaption { get; set; } = string.Empty;   // For player page subtitle

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackNumberText))]
    [NotifyPropertyChangedFor(nameof(Duration))]
    [NotifyPropertyChangedFor(nameof(MediaType))]
    public partial MediaInfo MediaInfo { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MainArtist))]
    public partial ArtistViewModel[] Artists { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    private WeakReference<BitmapImage>? _thumbnailRef;

    /// <summary>
    /// Caps concurrent thumbnail work across every instance.
    /// </summary>
    /// <remarks>
    /// A grid realizes dozens of containers at once and each one opened the shell thumbnail
    /// provider and decoded a bitmap with no limit; the native decoders are not hardened against
    /// that burst. The gate is held across the whole load (including the decode), so the decode
    /// step is bounded by the same permit - it must not take a second one, or four waiters that
    /// already hold it would deadlock the UI thread.
    /// </remarks>
    private static readonly SemaphoreSlim ThumbnailGate = new(4, 4);

    public MediaViewModel(MediaViewModel source)
    {
        Name = source.Name;
        _thumbnailRef = source._thumbnailRef;
        MediaInfo = source.MediaInfo;
        Artists = source.Artists;
        Album = source.Album;
        Caption = source.Caption;
        AltCaption = source.AltCaption;
        _options = new List<string>(source.Options);
        Options = new ReadOnlyCollection<string>(_options);
        Source = source.Source;
        DateAdded = source.DateAdded;
        IsFromLibrary = source.IsFromLibrary;
        DetailsLoaded = source.DetailsLoaded;
    }

    public MediaViewModel(IMediaSource source, MediaInfo? mediaInfo = null)
    {
        Source = source;
        DateAdded = DateTimeOffset.Now;
        Name = source.Name;
        MediaInfo = mediaInfo ?? new MediaInfo(source.MediaType);
        Artists = Array.Empty<ArtistViewModel>();
        _options = new List<string>();
        Options = new ReadOnlyCollection<string>(_options);
        AltCaption = source.Name;
    }

    partial void OnMediaInfoChanged(MediaInfo value)
    {
        UpdateCaptions();
    }

    /// <summary>
    /// Appends FFmpeg AVFormat options for this item, e.g. <c>user_agent=...</c> for a stream
    /// behind a hotlink check.
    /// </summary>
    /// <remarks>
    /// <see cref="Options"/> is a read-only view over the backing list, so appending here is
    /// picked up by <c>PlayQueueBufferManager</c> without replacing the collection. Duplicates are
    /// ignored because the same options can arrive from a saved stream and from the queue.
    /// </remarks>
    public void AddOptions(IEnumerable<string>? options)
    {
        if (options is null) return;

        foreach (string option in options)
        {
            if (string.IsNullOrWhiteSpace(option)) continue;
            if (!_options.Contains(option)) _options.Add(option);
        }
    }

    public override string ToString()
    {
        return $"{Name}; {Caption}";
    }

    public void SetOptions(string options)
    {
        string[] opts = options.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(o => o.StartsWith(':') && o.Length > 1).ToArray();

        // Check if new options and existing options are the same
        if (opts.Length == _options.Count && opts.SequenceEqual(_options))
        {
            return;
        }

        _options.Clear();
        _options.AddRange(opts);
    }

    public void UpdateSource(StorageFile file)
    {
        if (Source is IFileMediaSource fileSource)
        {
            fileSource.UpdateStorageFile(file);
        }
        AltCaption = file.Name;
    }

    /// <summary>
    /// Loads media metadata from <see cref="Source"/> using <paramref name="metadataService"/> and updates <see cref="MediaInfo"/>.
    /// </summary>
    /// <param name="metadataService">The source metadata service used to extract media metadata.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task LoadDetailsAsync(ISourceMetadataService metadataService, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadataService);
        DetailsLoaded = true;

        if (await metadataService.GetMediaInfoAsync(Source, cancellationToken) is { } parsedInfo)
        {
            ApplyMediaInfo(parsedInfo);
        }
    }

    /// <summary>
    /// Loads media metadata from <paramref name="item"/> using <paramref name="metadataService"/> and updates <see cref="MediaInfo"/>.
    /// </summary>
    /// <param name="metadataService">The playback metadata service used to extract media metadata.</param>
    /// <param name="item">The playback item to inspect.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task LoadDetailsAsync(IPlaybackMetadataService metadataService, IPlaybackItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadataService);
        ArgumentNullException.ThrowIfNull(item);

        if (await metadataService.GetMediaInfoAsync(item, cancellationToken) is { } parsedInfo)
        {
            if (Source is not IFileMediaSource)
            {
                DetailsLoaded = true;
            }

            ApplyMediaInfo(parsedInfo);
        }
    }

    private void ApplyMediaInfo(MediaInfo parsedInfo)
    {
        if (MediaType != MediaPlaybackType.Unknown)
        {
            parsedInfo.MediaType = MediaType;
        }
        else if (Source.MediaType != MediaPlaybackType.Unknown)
        {
            parsedInfo.MediaType = Source.MediaType;
        }

        MediaInfo = MediaInfo.MergeSelective(parsedInfo);
    }

    public async Task LoadThumbnailAsync()
    {
        if (Thumbnail is not null) return;

        // A scan in progress rebuilds the library every few seconds (LibraryService reports a
        // snapshot every 5 s), so each rebuild would queue a fresh thumbnail burst on top of the
        // scan's own shell property queries. Defer instead: the rebuild that follows the scan
        // re-triggers the realized containers, so the art still arrives once the library settles.
        if (IsLibraryLoading()) return;

        // Default context capture on purpose: the body assigns Thumbnail, which is bound to the UI.
        await ThumbnailGate.WaitAsync();
        try
        {
            await LoadThumbnailCoreAsync();
        }
        finally
        {
            ThumbnailGate.Release();
        }
    }

    private static bool IsLibraryLoading()
    {
        try
        {
            LibraryContext? library = Ioc.Default.GetService<LibraryContext>();
            return library is { IsLoadingMusic: true } or { IsLoadingVideos: true };
        }
        catch (Exception)
        {
            // No service provider (plain test host): there is no scan to defer to.
            return false;
        }
    }

    private async Task LoadThumbnailCoreAsync()
    {
        // Artwork the metadata service already took off the container while the file was playing.
        if (MediaInfo.ArtworkData is { Length: > 0 } artworkData)
        {
            if (await DecodeImageBytesAsync(artworkData, Location) is { } embedded)
            {
                PlaybackTrace.Write($"thumbnail: embedded artwork decoded, {artworkData.Length} bytes");
                Thumbnail = embedded;
                return;
            }

            PlaybackTrace.Write($"thumbnail: embedded artwork rejected, {artworkData.Length} bytes");
        }

        StorageFile? file = Source is IFileMediaSource fileSource
            ? await fileSource.GetStorageFileAsync()
            : null;

        // Album art for music never comes from the shell: without package identity the thumbnail
        // provider answers audio files with the generic file-type icon, not the cover. A cover image
        // next to the tracks (and, for multi-disc albums, one folder up) then the artwork embedded in
        // the container are the sources that actually produce an image.
        if (MediaType != MediaPlaybackType.Video && file is not null)
        {
            if (await AlbumArtworkLoader.LoadAsync(file) is { Length: > 0 } coverData)
            {
                if (await DecodeImageBytesAsync(coverData, Location) is { } cover)
                {
                    PlaybackTrace.Write($"thumbnail: album artwork decoded, {coverData.Length} bytes");
                    Thumbnail = cover;
                    return;
                }

                PlaybackTrace.Write($"thumbnail: album artwork rejected, {coverData.Length} bytes");
            }
            else
            {
                PlaybackTrace.Write("thumbnail: no cover file and no embedded artwork");
            }
        }

        if (file is not null)
        {
            // Video frames only come from the shell with a video-shaped mode: SingleItem answers a
            // video with the generic file-type icon, while VideosView decodes an actual frame.
            ThumbnailMode mode = GetShellThumbnailMode(MediaType);

            // 640 covers a 300dp tile on a 2x display; asking for the full frame is wasted work
            // because the thumbnail is decoded down to the tile height anyway.
            using var source = await GetThumbnailSourceAsync(file, mode, 640);
            if (source is null)
            {
                PlaybackTrace.Write($"thumbnail: no shell thumbnail ({mode})");
                return;
            }

            BitmapImage image = new()
            {
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelHeight = 300
            };

            try
            {
                await image.SetSourceAsync(source);
            }
            catch (Exception e)
            {
                // WinRT component not found exception???
                StartupTrace.Write($"thumb.shell.failed (setSource, file={file.Name})", e);
                return;
            }

            PlaybackTrace.Write($"thumbnail: shell thumbnail decoded ({mode})");
            Thumbnail = image;
        }
        else if (MediaInfo.ArtworkUri is { } artworkUri)
        {
            Thumbnail = new BitmapImage(artworkUri)
            {
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelHeight = 300
            };
        }
    }

    private static async Task<BitmapImage?> DecodeImageBytesAsync(byte[] imageData, string? origin = null)
    {
        try
        {
            using InMemoryRandomAccessStream stream = new();
            using (DataWriter writer = new(stream))
            {
                writer.WriteBytes(imageData);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            BitmapImage image = new()
            {
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelHeight = 300
            };

            await image.SetSourceAsync(stream);
            return image;
        }
        catch (Exception e)
        {
            // Unknown or corrupt codec for the embedded cover; fall back to the next source.
            StartupTrace.Write($"thumb.decode.failed (origin={origin ?? "?"}, bytes={imageData.Length})", e);
            return null;
        }
    }

    public async Task<IRandomAccessStream?> GetThumbnailSourceAsync()
    {
        // Same permit as LoadThumbnailAsync: this is the other entry point that asks the shell
        // provider for a decoded image (album page background).
        await ThumbnailGate.WaitAsync();
        try
        {
            return Source is IFileMediaSource fileSource && fileSource.StorageFile is { } file
                ? await GetThumbnailSourceAsync(file)
                : await GetThumbnailSourceLazyAsync();
        }
        finally
        {
            ThumbnailGate.Release();
        }
    }

    private async Task<IRandomAccessStream?> GetThumbnailSourceLazyAsync()
    {
        if (Source is IFileMediaSource fileSource && await fileSource.GetStorageFileAsync() is { } file)
        {
            return await GetThumbnailSourceAsync(file);
        }

        return null;
    }

    /// <summary>
    /// Picks the shell thumbnail mode for a media type.
    /// </summary>
    /// <remarks>
    /// <see cref="ThumbnailMode.SingleItem"/> is wrong for video: the shell answers a video request
    /// in that mode with the generic file-type icon, so the tile showed no frame at all. A
    /// video-shaped mode (<see cref="ThumbnailMode.VideosView"/>) decodes an actual frame.
    /// </remarks>
    public static ThumbnailMode GetShellThumbnailMode(MediaPlaybackType mediaType) =>
        mediaType == MediaPlaybackType.Video ? ThumbnailMode.VideosView : ThumbnailMode.SingleItem;

    private static async Task<IRandomAccessStream?> GetThumbnailSourceAsync(
        StorageFile file,
        ThumbnailMode mode = ThumbnailMode.SingleItem,
        uint requestedSize = 1280)
    {
        if (!file.IsAvailable)
            return null;

        try
        {
            // SingleItem returns embedded album art with the original aspect ratio, which is what
            // the album page needs; a video needs VideosView to get a frame instead of an icon.
            // https://learn.microsoft.com/windows/apps/develop/files/thumbnails
            StorageItemThumbnail? source =
                await file.GetThumbnailAsync(mode, requestedSize, ThumbnailOptions.UseCurrentScale);
            if (source is { Type: ThumbnailType.Image })
            {
                return source;
            }
        }
        catch (Exception e)
        {
            //// System.Exception: The data necessary to complete this operation is not yet available.
            //if (e.HResult != unchecked((int)0x8000000A) &&
            //    // System.Exception: The RPC server is unavailable.
            //    e.HResult != unchecked((int)0x800706BA))
            //    _logger.LogError(e, "Failed to load the image thumbnail.");
            StartupTrace.Write($"thumb.shell.failed ({mode}, file={file.Name})", e);
        }

        return null;
    }

    private void UpdateCaptions()
    {
        string title = MediaType switch
        {
            MediaPlaybackType.Video => MediaInfo.VideoProperties.Title,
            _ => !string.IsNullOrEmpty(MediaInfo.MusicProperties.Title)
                ? MediaInfo.MusicProperties.Title
                : MediaInfo.VideoProperties.Title
        };

        if (!string.IsNullOrEmpty(title))
        {
            Name = title;
        }

        if (Duration > TimeSpan.Zero)
        {
            Caption = Humanizer.ToDuration(Duration);
        }

        MusicInfo musicProperties = MediaInfo.MusicProperties;
        if (!string.IsNullOrEmpty(musicProperties.Artist))
        {
            Caption = musicProperties.Artist;
            AltCaption = string.IsNullOrEmpty(musicProperties.Album)
                ? musicProperties.Artist
                : $"{musicProperties.Artist} – {musicProperties.Album}";
        }
        else if (!string.IsNullOrEmpty(musicProperties.Album))
        {
            AltCaption = musicProperties.Album;
        }

        if (Name == AltCaption)
        {
            AltCaption = string.Empty;
        }
    }
}
