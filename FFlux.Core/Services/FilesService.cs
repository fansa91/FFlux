using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Helpers;
using Screenbox.Core.MediaSources;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.Storage.Search;
using Windows.System;

namespace Screenbox.Core.Services;

public sealed class FilesService : IFilesService
{
    private readonly ILogger<FilesService> _logger;
    private readonly IDatabaseService _databaseService;

    public FilesService(ILogger<FilesService> logger, IDatabaseService databaseService)
    {
        _logger = logger;
        _databaseService = databaseService;
    }

    public async Task<StorageFileQueryResult?> GetNeighboringFilesQueryAsync(StorageFile file, QueryOptions? options = null)
    {
        try
        {
            StorageFolder? parent = await file.GetParentAsync();
            options ??= new QueryOptions(CommonFileQuery.DefaultQuery, FilesHelpers.SupportedFormats);
            StorageFileQueryResult? queryResult = parent?.CreateFileQueryWithOptions(options);
            return queryResult;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<StorageFile?> GetNextFileAsync(IStorageFile currentFile, StorageFileQueryResult neighboringFilesQuery)
    {
        // Due to limitations with NeighboringFilesQuery, manually find the next supported file
        uint startIndex = await neighboringFilesQuery.FindStartIndexAsync(currentFile);
        if (startIndex == uint.MaxValue) return null;
        startIndex += 1;

        // The following line return a native vector view.
        // It does not fetch all the files in the directory at once.
        // No need for manual paging!
        IReadOnlyList<StorageFile> files = await neighboringFilesQuery.GetFilesAsync(startIndex, uint.MaxValue);
        return files.FirstOrDefault(x => x.IsSupported());
    }

    public async Task<StorageFile?> GetPreviousFileAsync(IStorageFile currentFile, StorageFileQueryResult neighboringFilesQuery)
    {
        // Due to limitations with NeighboringFilesQuery, manually find the previous supported file
        uint startIndex = await neighboringFilesQuery.FindStartIndexAsync(currentFile);
        if (startIndex == uint.MaxValue) return null;

        // The following line return a native vector view.
        // It does not fetch all the files in the directory at once.
        // No need for manual paging!
        IReadOnlyList<StorageFile> files = await neighboringFilesQuery.GetFilesAsync(0, startIndex);
        return files.LastOrDefault(x => x.IsSupported());
    }

    public StorageItemQueryResult GetSupportedItems(StorageFolder folder)
    {
        // Don't use indexer when querying. Potential incomplete result.
        QueryOptions queryOptions = new(CommonFileQuery.DefaultQuery, FilesHelpers.SupportedFormats.ToArray());
        return folder.CreateItemQueryWithOptions(queryOptions);
    }

    public IAsyncOperation<uint> GetSupportedItemCountAsync(StorageFolder folder)
    {
        QueryOptions queryOptions = new(CommonFileQuery.DefaultQuery, FilesHelpers.SupportedFormats.ToArray());
        return folder.CreateItemQueryWithOptions(queryOptions).GetItemCountAsync();
    }

    public IAsyncOperation<StorageFile> PickFileAsync(params string[] formats)
    {
        FileOpenPicker picker = GetFilePickerForFormats(formats);
        return picker.PickSingleFileAsync();
    }

    public IAsyncOperation<IReadOnlyList<StorageFile>> PickMultipleFilesAsync(params string[] formats)
    {
        FileOpenPicker picker = GetFilePickerForFormats(formats);
        return picker.PickMultipleFilesAsync();
    }

    public IAsyncOperation<StorageFile> PickSaveFileAsync(string suggestedFileName,
        IDictionary<string, IList<string>> fileTypes, PickerLocationId startLocation = PickerLocationId.ComputerFolder)
    {
        FileSavePicker picker = AppWindowInterop.Initialize(new FileSavePicker()
        {
            SuggestedStartLocation = startLocation,
            SuggestedFileName = suggestedFileName
        });

        foreach (var fileType in fileTypes)
        {
            picker.FileTypeChoices.Add(fileType.Key, fileType.Value);
        }

        return picker.PickSaveFileAsync();
    }

    public IAsyncOperation<StorageFolder> PickFolderAsync()
    {
        FolderPicker picker = AppWindowInterop.Initialize(new FolderPicker()
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        });

        foreach (string supportedFormat in FilesHelpers.SupportedFormats)
        {
            picker.FileTypeFilter.Add(supportedFormat);
        }

        return picker.PickSingleFolderAsync();
    }

    public async Task OpenFileLocationAsync(string path)
    {
        string? folderPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folderPath))
            await Launcher.LaunchFolderPathAsync(folderPath);
    }

    public async Task OpenFileLocationAsync(StorageFile file)
    {
        StorageFolder? folder = await file.GetParentAsync();
        if (folder == null)
        {
            await OpenFileLocationAsync(file.Path);
        }
        else
        {
            FolderLauncherOptions options = AppWindowInterop.Initialize(new FolderLauncherOptions());
            options.ItemsToSelect.Add(file);
            await Launcher.LaunchFolderAsync(folder, options);
        }
    }

    public void AddToRecent(IStorageItem item)
    {
        // Callers are on a synchronous path, and the write is best-effort: the only thing it feeds
        // is the ordering of the home page list. Tracking swallows its own exceptions, so nothing
        // can surface as an unobserved task exception.
        _ = TrackRecentAsync(item);
    }

    public async Task AddToRecentAsync(IMediaSource? source, CancellationToken cancellationToken = default)
    {
        try
        {
            if (source is IFileMediaSource fileSource && await fileSource.GetStorageFileAsync() is { } file)
            {
                if (cancellationToken.IsCancellationRequested) return;
                await TrackRecentAsync(file).ConfigureAwait(false);
            }
        }
        catch
        {
            // Silently ignore — recent-files tracking is best-effort.
        }
    }

    public async Task<IReadOnlyList<StorageFile>> GetRecentFilesAsync(int limit)
    {
        IReadOnlyList<string> paths;
        try
        {
            paths = await _databaseService.GetRecentItemPathsAsync(limit).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to read the recent list.");
            return Array.Empty<StorageFile>();
        }

        if (paths.Count == 0) return Array.Empty<StorageFile>();

        // Resolve in parallel but keep the database order: the list is ordered by recency.
        var resolved = await Task.WhenAll(paths.Select(ResolveRecentAsync)).ConfigureAwait(false);

        var files = new List<StorageFile>(resolved.Length);
        var gone = new List<string>();
        for (int i = 0; i < resolved.Length; i++)
        {
            if (resolved[i].File is { } file)
            {
                files.Add(file);
            }
            else if (resolved[i].IsGone)
            {
                gone.Add(paths[i]);
            }
        }

        // A file the user deleted or moved away disappears instead of lingering as a dead tile.
        foreach (string path in gone)
        {
            try
            {
                await _databaseService.RemoveRecentItemAsync(path).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "Failed to forget missing recent file '{Path}'.", path);
            }
        }

        return files;
    }

    public async Task RemoveFromRecentAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            await _databaseService.RemoveRecentItemAsync(path).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Failed to forget recent file '{Path}'.", path);
        }
    }

    public async Task ClearRecentAsync()
    {
        try
        {
            await _databaseService.ClearRecentItemsAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to clear the recent list.");
        }
    }

    private async Task TrackRecentAsync(IStorageItem item)
    {
        // Only files are playable; a folder or playlist handed in by QueueService has no place in
        // the list. URI sources have no path and are not tracked either.
        if (item is not StorageFile file) return;
        string path = file.Path;
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            await _databaseService
                .TouchRecentItemAsync(path, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Failed to record '{Path}' in the recent list.", path);
        }
    }

    /// <summary>
    /// Resolves a stored path, distinguishing "the file is gone" (drop the row) from "the file is
    /// temporarily unreachable" (keep the row, hide the entry).
    /// </summary>
    private static async Task<(StorageFile? File, bool IsGone)> ResolveRecentAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                // When the parent folder is missing too, the whole volume is probably offline or
                // the folder was moved. Keep the row so the entry comes back with the drive.
                string? folder = Path.GetDirectoryName(path);
                bool parentMissing = folder is { Length: > 0 } && !Directory.Exists(folder);
                return (null, !parentMissing);
            }

            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            return file.IsAvailable ? (file, false) : (null, false);
        }
        catch (Exception)
        {
            // Locked, permission denied, unreadable format for the shell: keep the row anyway.
            return (null, false);
        }
    }

    public IAsyncOperation<StorageFolder> GetFrameCaptureFolderAsync(string token)
    {
        return GetFrameCaptureFolderFromTokenAsync(token).AsAsyncOperation();
    }

    private static async Task<StorageFolder> GetFrameCaptureFolderFromTokenAsync(string token)
    {
        StorageFolder? tokenFolder = await AppAccessCache.GetFolderFromFutureAccessAsync(token);
        if (tokenFolder is not null) return tokenFolder;

        try
        {
            StorageLibrary pictureLibrary = await StorageLibrary.GetLibraryAsync(KnownLibraryId.Pictures);
            StorageFolder savePicturesFolder = pictureLibrary.SaveFolder;

            return await savePicturesFolder.CreateFolderAsync("FFlux", CreationCollisionOption.OpenIfExists);
        }
        catch (UnauthorizedAccessException)
        {
            // Pictures library permission missing or access denied.
        }

        // Fall back to captures folder.
        return KnownFolders.AppCaptures;
    }

    private FileOpenPicker GetFilePickerForFormats(IReadOnlyCollection<string> formats)
    {
        FileOpenPicker picker = new()
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };

        IEnumerable<string> fileTypes = formats;
        if (formats.Count == 0)
        {
            fileTypes = FilesHelpers.SupportedFormats;
            picker.FileTypeFilter.Add("*");
        }

        foreach (string? fileType in fileTypes)
        {
            picker.FileTypeFilter.Add(fileType);
        }

        // Desktop pickers need an owner HWND; without it PickSingleFileAsync throws
        // 0x80070578 (invalid window handle) in an unpackaged process.
        return AppWindowInterop.Initialize(picker);
    }
}
