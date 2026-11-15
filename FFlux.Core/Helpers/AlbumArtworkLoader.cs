using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFmpegInteropX;
using Screenbox.Core.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Resolves album art for a music file from sources that keep working in an unpackaged process.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StorageFile.GetThumbnailAsync"/> cannot be used for album art here: without package
/// identity the shell answers every audio file with the generic file-type icon
/// (<c>ThumbnailType.Icon</c>, a 1280x1280 bitmap placeholder) instead of the embedded cover, so
/// the cover silently disappears even for tracks that carry one.
/// </para>
/// <para>
/// Two sources remain, and they complement each other: a cover image next to the file (the usual
/// shape of a downloaded album, including rips whose tracks carry no embedded artwork) and the
/// artwork embedded in the container, which FFmpeg reads directly.
/// </para>
/// </remarks>
public static class AlbumArtworkLoader
{
    /// <summary>Cover file stems, in priority order.</summary>
    private static readonly string[] CoverFileNames =
    {
        "cover", "folder", "front", "albumart", "album", "artwork"
    };

    private static readonly string[] CoverFileExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

    /// <summary>Guards against tiny files such as thumbnails or web art accidentally named "cover".</summary>
    private const long MinimumCoverFileSize = 2048;

    /// <summary>
    /// How many parent folders to inspect above the file's own folder. Album folders are often
    /// split into CD1/CD2 subfolders that carry the track files while the cover sits one level up.
    /// </summary>
    private const int MaxParentSearchDepth = 1;

    // Folder path -> cover file (null when the folder was already searched and has none). Cached so
    // that scrolling a grid does not repeat the directory listing for every track of an album.
    private static readonly ConcurrentDictionary<string, StorageFile?> CoverFileCache =
        new(StringComparer.OrdinalIgnoreCase);

    // Folder path -> "the container has no embedded artwork", so a track that has neither a cover
    // file nor embedded art is not opened by FFmpeg again on every request.
    private static readonly ConcurrentDictionary<string, bool> NoEmbeddedArtworkCache =
        new(StringComparer.OrdinalIgnoreCase);

    // Decoded bytes, keyed by the *image source* path (the cover file, or the track itself when
    // the art is embedded). An album shares one cover across all of its tracks, so keying by the
    // track would keep N identical copies of the same bitmap alive.
    private static readonly ArtworkByteCache ArtworkCache = new();

    // Track path -> image source path. Lets the next track of an album hit the byte cache without
    // repeating the folder lookup.
    private static readonly ConcurrentDictionary<string, string> SourceKeyByTrack =
        new(StringComparer.OrdinalIgnoreCase);

    // Coalesces concurrent requests for the same track. Thumbnail, now-playing and SMTC flyout
    // all ask at nearly the same moment; without this they would each decode independently.
    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> InFlight =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the raw bytes of the album art for <paramref name="file"/>, or <see langword="null"/>
    /// when no source produced an image. The returned array is shared with the cache and must be
    /// treated as read-only.
    /// </summary>
    public static Task<byte[]?> LoadAsync(StorageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        string trackPath = SafePath(file);
        if (trackPath.Length == 0) return LoadCoreAsync(file, string.Empty);

        if (SourceKeyByTrack.TryGetValue(trackPath, out string? sourceKey)
            && ArtworkCache.TryGet(sourceKey) is { } cached)
        {
            return Task.FromResult<byte[]?>(cached);
        }

        Lazy<Task<byte[]?>> lazy = InFlight.GetOrAdd(
            trackPath,
            _ => new Lazy<Task<byte[]?>>(
                () => LoadCoreAsync(file, trackPath),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return AwaitAndReleaseAsync(trackPath, lazy);
    }

    private static async Task<byte[]?> AwaitAndReleaseAsync(string trackPath, Lazy<Task<byte[]?>> lazy)
    {
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            if (InFlight.TryGetValue(trackPath, out Lazy<Task<byte[]?>>? current)
                && ReferenceEquals(current, lazy))
            {
                InFlight.TryRemove(trackPath, out _);
            }
        }
    }

    private static async Task<byte[]?> LoadCoreAsync(StorageFile file, string trackPath)
    {
        if (await FindCoverFileAsync(file) is { } coverFile)
        {
            string coverPath = SafePath(coverFile);
            string key = coverPath.Length > 0 ? coverPath : trackPath;
            if (key.Length > 0 && ArtworkCache.TryGet(key) is { } hit)
            {
                RememberSource(trackPath, key);
                return hit;
            }

            if (await ReadAllBytesAsync(coverFile) is { Length: > 0 } coverData)
            {
                if (key.Length > 0)
                {
                    ArtworkCache.Store(key, coverData);
                    RememberSource(trackPath, key);
                }

                return coverData;
            }
        }

        byte[]? artwork = await ExtractEmbeddedArtworkAsync(file);
        if (artwork is { Length: > 0 } && trackPath.Length > 0)
        {
            ArtworkCache.Store(trackPath, artwork);
            RememberSource(trackPath, trackPath);
        }

        return artwork;
    }

    private static void RememberSource(string trackPath, string sourceKey)
    {
        if (trackPath.Length > 0) SourceKeyByTrack[trackPath] = sourceKey;
    }

    private static string SafePath(StorageFile file)
    {
        try
        {
            return file.Path ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Drops every in-memory artwork lookup. Intended for tests and low-memory scenarios.</summary>
    public static void ClearCaches()
    {
        CoverFileCache.Clear();
        NoEmbeddedArtworkCache.Clear();
        SourceKeyByTrack.Clear();
        InFlight.Clear();
        ArtworkCache.Clear();
    }

    /// <summary>
    /// Looks for a cover image in the file's folder and, failing that, in its parent folder.
    /// </summary>
    public static async Task<StorageFile?> FindCoverFileAsync(StorageFile file)
    {
        StorageFolder? folder;
        try
        {
            folder = await file.GetParentAsync();
        }
        catch (Exception)
        {
            return null;
        }

        for (int depth = 0; folder is not null && depth <= MaxParentSearchDepth; depth++)
        {
            if (await FindCoverInFolderAsync(folder) is { } cover)
            {
                return cover;
            }

            try
            {
                folder = await folder.GetParentAsync();
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }

    private static async Task<StorageFile?> FindCoverInFolderAsync(StorageFolder folder)
    {
        string key = folder.Path;
        if (key.Length > 0 && CoverFileCache.TryGetValue(key, out StorageFile? cached))
        {
            return cached;
        }

        StorageFile? found = null;
        try
        {
            IReadOnlyList<StorageFile> files = await folder.GetFilesAsync();
            Dictionary<string, StorageFile> byName = new(StringComparer.OrdinalIgnoreCase);
            foreach (StorageFile item in files)
            {
                byName[item.Name] = item;
            }

            foreach (string candidateName in OrderCoverCandidates(files.Select(f => f.Name)))
            {
                if (!byName.TryGetValue(candidateName, out StorageFile? candidate)) continue;
                if (!await IsLargeEnoughAsync(candidate)) continue;

                found = candidate;
                break;
            }
        }
        catch (Exception)
        {
            // An unreadable folder simply has no cover; nothing to cache in that case.
            return null;
        }

        if (key.Length > 0) CoverFileCache[key] = found;
        return found;
    }

    /// <summary>
    /// Orders the files of a folder by how likely each one is to be that folder's cover.
    /// </summary>
    /// <remarks>
    /// Separated from the storage calls so the priority rule - stem first, then extension - stays
    /// testable: "folder.jpg" beats "cover.png", and any non-image is never a candidate.
    /// </remarks>
    public static IReadOnlyList<string> OrderCoverCandidates(IEnumerable<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(fileNames);

        List<string> names = fileNames.ToList();
        List<string> ordered = new();
        foreach (string stem in CoverFileNames)
        {
            foreach (string extension in CoverFileExtensions)
            {
                string candidate = stem + extension;
                foreach (string fileName in names)
                {
                    if (string.Equals(fileName, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        ordered.Add(fileName);
                        break;
                    }
                }
            }
        }

        return ordered;
    }

    private static async Task<bool> IsLargeEnoughAsync(StorageFile file)
    {
        try
        {
            return (await file.GetBasicPropertiesAsync()).Size >= MinimumCoverFileSize;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<byte[]?> ReadAllBytesAsync(StorageFile file)
    {
        try
        {
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            if (stream.Size == 0 || stream.Size > int.MaxValue) return null;

            byte[] buffer = new byte[(int)stream.Size];
            using DataReader reader = new(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(buffer);
            return buffer;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<byte[]?> ExtractEmbeddedArtworkAsync(StorageFile file)
    {
        string key = string.Empty;
        try
        {
            key = (await file.GetParentAsync())?.Path ?? string.Empty;
        }
        catch (Exception)
        {
            // No folder means no reason to remember the outcome.
        }

        if (key.Length > 0 && NoEmbeddedArtworkCache.ContainsKey(key))
        {
            return null;
        }

        byte[]? artwork = null;
        try
        {
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            MediaSourceConfig config = FFmpegMediaSourceConfigFactory.Create();
            using FFmpegMediaSource source = await FFmpegMediaSource.CreateFromStreamAsync(stream, config);
            if (source.HasThumbnail)
            {
                MediaThumbnailData thumbnail = source.ExtractThumbnail();
                try
                {
                    IBuffer? buffer = thumbnail.Buffer;
                    if (buffer is not null && buffer.Length > 0 && buffer.Length <= int.MaxValue)
                    {
                        byte[] bytes = new byte[buffer.Length];
                        using DataReader reader = DataReader.FromBuffer(buffer);
                        reader.ReadBytes(bytes);
                        artwork = bytes;
                    }
                }
                finally
                {
                    thumbnail.Dispose();
                }
            }
        }
        catch (Exception)
        {
            // A malformed cover must not break thumbnail loading; the caller falls back to a glyph.
            return null;
        }

        // Remember only the negative result: an album without embedded art is opened once, while
        // keeping the extracted bytes alive would cost hundreds of megabytes on a large library.
        if (artwork is null && key.Length > 0) NoEmbeddedArtworkCache[key] = true;
        return artwork;
    }
}
