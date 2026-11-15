using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Factories;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.ViewModels;
using Windows.Foundation.Metadata;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;

namespace Screenbox.Core.Services;

/// <summary>
/// Stateless service for library management operations
/// </summary>
public sealed class LibraryService : ILibraryService
{
    private bool SearchRemovableStorage => _librarySettings.SearchRemovableStorage && SystemInformation.IsXbox;

    private static readonly string[] CustomPropertyKeys = { SystemProperties.Title };
    private readonly LibrarySettingsContext _librarySettings;
    private readonly ISourceMetadataService _sourceMetadataService;
    private readonly IDatabaseService _databaseService;
    private readonly MediaViewModelFactory _mediaFactory;
    private readonly ILogger<LibraryService> _logger;

    public LibraryService(
        LibrarySettingsContext librarySettings,
        ISourceMetadataService sourceMetadataService,
        IDatabaseService databaseService,
        MediaViewModelFactory mediaFactory,
        ILogger<LibraryService> logger)
    {
        _librarySettings = librarySettings;
        _sourceMetadataService = sourceMetadataService;
        _databaseService = databaseService;
        _mediaFactory = mediaFactory;
        _logger = logger;
    }

    public IReadOnlyList<StorageFileQueryResult> CreateMusicLibraryQueries(IReadOnlyList<StorageFolder> roots, bool useIndexer)
    {
        // Uses the same query options as the service's fetch logic. Centralized to avoid duplication.
        QueryOptions queryOptions = new(CommonFileQuery.OrderByTitle, FilesHelpers.SupportedAudioFormats.ToArray())
        {
            IndexerOption = useIndexer ? IndexerOption.UseIndexerWhenAvailable : IndexerOption.DoNotUseIndexer
        };
        queryOptions.SetPropertyPrefetch(
            PropertyPrefetchOptions.BasicProperties | PropertyPrefetchOptions.MusicProperties,
            CustomPropertyKeys);

        return CreateQueries(roots, queryOptions);
    }

    public IReadOnlyList<StorageFileQueryResult> CreateVideosLibraryQueries(IReadOnlyList<StorageFolder> roots, bool useIndexer)
    {
        // Uses the same query options as the service's fetch logic. Centralized to avoid duplication.
        QueryOptions queryOptions = new(CommonFileQuery.OrderByName, FilesHelpers.SupportedVideoFormats.ToArray())
        {
            IndexerOption = useIndexer ? IndexerOption.UseIndexerWhenAvailable : IndexerOption.DoNotUseIndexer
        };
        queryOptions.SetPropertyPrefetch(
            PropertyPrefetchOptions.BasicProperties | PropertyPrefetchOptions.VideoProperties,
            CustomPropertyKeys);

        return CreateQueries(roots, queryOptions);
    }

    private List<StorageFileQueryResult> CreateQueries(IReadOnlyList<StorageFolder> roots, QueryOptions queryOptions)
    {
        var queries = new List<StorageFileQueryResult>(roots.Count);
        foreach (StorageFolder root in roots)
        {
            try
            {
                queries.Add(root.CreateFileQueryWithOptions(queryOptions));
            }
            catch (Exception e)
            {
                // A folder that vanished between resolution and query creation simply has no files.
                _logger.LogWarning(e, "Skipping library folder '{Path}': no query could be created for it.", root.Path);
            }
        }

        return queries;
    }

    /// <summary>
    /// Lists every folder the library has to scan.
    /// </summary>
    /// <remarks>
    /// The folders reported by <see cref="StorageLibrary"/> are real paths in both the packaged and
    /// the unpackaged build, so they are the source of truth. An unpackaged process used to pick
    /// only the first of them, which silently hid everything kept in a second library folder;
    /// all of them are used now, plus the folders added by hand inside the app.
    /// </remarks>
    public async Task<IReadOnlyList<StorageFolder>> GetLibraryRootsAsync(StorageLibrary library, MediaPlaybackType mediaType)
    {
        List<string> libraryPaths = library.Folders.Select(f => f.Path).ToList();
        IReadOnlyList<string> extraPaths = await LoadExtraRootPathsAsync(mediaType);
        IReadOnlyList<string> merged = LibraryRootPaths.Merge(libraryPaths, extraPaths);

        List<StorageFolder> roots = await MaterializeAsync(merged);
        if (roots.Count > 0) return roots;

        // Nothing resolvable. A packaged app can still fall back to the known folder, which is a
        // virtual folder merging every folder of the library.
        try
        {
            roots.Add(mediaType == MediaPlaybackType.Video
                ? KnownFolders.VideosLibrary
                : KnownFolders.MusicLibrary);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "No usable root folder for the {MediaType} library.", mediaType);
        }

        return roots;
    }

    public async Task<bool> AddLibraryRootAsync(StorageLibrary library, MediaPlaybackType mediaType, StorageFolder folder)
    {
        string path = folder.Path;
        if (string.IsNullOrWhiteSpace(path)) return false;

        // Folders Windows already reports need no entry of their own: the scan covers them anyway,
        // and an entry would switch the change-tracker cache off for no gain.
        IReadOnlyList<StorageFolder> roots = await GetLibraryRootsAsync(library, mediaType);
        if (LibraryRootPaths.Covers(roots.Select(r => r.Path), path)) return false;

        try
        {
            return await _databaseService.AddLibraryRootAsync(mediaType, path);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to remember the {MediaType} library folder '{Path}'.", mediaType, path);
            return false;
        }
    }

    public async Task<bool> RemoveLibraryRootAsync(MediaPlaybackType mediaType, StorageFolder folder)
    {
        string path = folder.Path;
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            return await _databaseService.RemoveLibraryRootAsync(mediaType, path);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to forget the {MediaType} library folder '{Path}'.", mediaType, path);
            return false;
        }
    }

    private async Task<IReadOnlyList<string>> LoadExtraRootPathsAsync(MediaPlaybackType mediaType)
    {
        try
        {
            return await _databaseService.GetLibraryRootsAsync(mediaType);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to read the hand-added {MediaType} library folders.", mediaType);
            return Array.Empty<string>();
        }
    }

    private async Task<List<StorageFolder>> MaterializeAsync(IReadOnlyList<string> paths)
    {
        var folders = new List<StorageFolder>(paths.Count);
        foreach (string path in paths)
        {
            try
            {
                folders.Add(await StorageFolder.GetFolderFromPathAsync(path));
            }
            catch (Exception e)
            {
                // An unplugged drive or a deleted folder must not take the whole library down.
                _logger.LogWarning(e, "Library folder '{Path}' is not reachable and will be skipped.", path);
            }
        }

        return folders;
    }

    public async Task<StorageLibrary> InitializeMusicLibraryAsync()
    {
        // No need to add handler for StorageLibrary.DefinitionChanged
        var library = await StorageLibrary.GetLibraryAsync(KnownLibraryId.Music);
        try
        {
            // Null without package identity; there is nothing to enable in that case.
            library.ChangeTracker?.Enable();
        }
        catch (Exception)
        {
            // pass
        }

        return library;
    }

    public async Task<StorageLibrary> InitializeVideosLibraryAsync()
    {
        // No need to add handler for StorageLibrary.DefinitionChanged
        var library = await StorageLibrary.GetLibraryAsync(KnownLibraryId.Videos);
        try
        {
            // Null without package identity; there is nothing to enable in that case.
            library.ChangeTracker?.Enable();
        }
        catch (Exception)
        {
            // pass
        }

        return library;
    }

    public async Task<MusicLibrary> FetchMusicAsync(
        LibraryScanScope scope,
        bool useCache,
        CancellationToken cancellationToken,
        IProgress<MusicLibrary>? progress = null)
    {
        StorageLibrary library = scope.Library;
        StorageLibraryChangeTracker? libraryChangeTracker = library.ChangeTracker;
        StorageLibraryChangeReader? changeReader = null;

        // Without package identity the library reports no change tracker, so a cached snapshot can
        // never be checked against what is actually on disk. Rescanning is then the only correct
        // answer - a stale cache would hide every file the user has added since the last fetch.
        // The same holds once a hand-added folder is in play: the tracker only reports on the
        // folders Windows knows about, so a change in those folders would go unnoticed.
        useCache = useCache
            && libraryChangeTracker is not null
            && !SystemInformation.IsXbox
            && !await HasExtraRootsAsync(MediaPlaybackType.Music);
        bool hasCache = false;

        List<MediaViewModel> cachedSongs = new();
        List<MediaViewModel> songs = new();
        if (useCache)
        {
            RawCacheLoadResultDto cacheResult = await LoadRawMusicCacheAsync(cancellationToken);
            List<string> folderPaths = cacheResult.FolderPaths;
            List<RawMediaRecordDto> rawRecords = cacheResult.Records;
            if (rawRecords.Count > 0)
            {
                songs = CreateSongsFromRawRecords(rawRecords);
                hasCache = !AreLibraryPathsChanged(folderPaths, scope.Roots);

                if (hasCache)
                {
                    cachedSongs = new List<MediaViewModel>(songs);
                    try
                    {
                        libraryChangeTracker!.Enable();
                        changeReader = libraryChangeTracker.GetChangeReader();
                        hasCache = await TryResolveLibraryChangeAsync(songs, changeReader);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to resolve music library changes from the change tracker.");
                    }
                }
            }
        }

        var albumFactory = new AlbumViewModelFactory();
        var artistFactory = new ArtistViewModelFactory();

        if (!hasCache)
        {
            songs = new List<MediaViewModel>();
            // One folder can be reached twice when a hand-added folder overlaps a library folder on
            // a different casing or through a symlink; the set keeps the list free of duplicates.
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await BatchFetchMusicAsync(scope.Queries, songs, visited, albumFactory, artistFactory, cancellationToken, progress);

            if (SearchRemovableStorage && await KnownFolderAccess.HasAccessAsync(KnownFolderId.RemovableDevices))
            {
                IReadOnlyList<StorageFileQueryResult> removableQueries = [CreateRemovableStorageMusicQuery()];
                await BatchFetchMusicAsync(removableQueries, songs, visited, albumFactory, artistFactory, cancellationToken, progress);
            }
        }
        else
        {
            // Cache path: build associations in one pass (fast, no async detail loading needed)
            foreach (MediaViewModel song in songs)
            {
                song.IsFromLibrary = true;
                albumFactory.AddSong(song);
                artistFactory.AddSong(song);
                song.Album = albumFactory.SongsToAlbums[song];
                song.Artists = artistFactory.SongsToArtists[song].ToArray();
                song.Album.UpdateProperties();
            }
        }

        var result = new MusicLibrary(
            songs,
            albumFactory.Albums,
            artistFactory.Artists,
            ExtractGenres(songs),
            albumFactory.UnknownAlbum,
            artistFactory.UnknownArtist);

        if (!hasCache || !songs.SequenceEqual(cachedSongs))
        {
            await CacheSongsAsync(scope.Roots, songs, cancellationToken);
        }

        if (hasCache && changeReader != null)
        {
            await changeReader.AcceptChangesAsync();
        }
        else
        {
            libraryChangeTracker?.Reset();
        }

        return result;
    }

    public async Task<VideosLibrary> FetchVideosAsync(
        LibraryScanScope scope,
        bool useCache,
        CancellationToken cancellationToken,
        IProgress<VideosLibrary>? progress = null)
    {
        StorageLibrary library = scope.Library;
        StorageLibraryChangeTracker? libraryChangeTracker = library.ChangeTracker;
        StorageLibraryChangeReader? changeReader = null;

        // See FetchMusicAsync: no identity means no change tracker, and a cache that cannot be
        // validated has to be treated as missing. Hand-added folders invalidate it as well.
        useCache = useCache
            && libraryChangeTracker is not null
            && !SystemInformation.IsXbox
            && !await HasExtraRootsAsync(MediaPlaybackType.Video);
        bool hasCache = false;

        List<MediaViewModel> cachedVideos = new();
        List<MediaViewModel> videos = new();
        if (useCache)
        {
            RawCacheLoadResultDto cacheResult = await LoadRawVideoCacheAsync(cancellationToken);
            List<string> folderPaths = cacheResult.FolderPaths;
            List<RawMediaRecordDto> rawRecords = cacheResult.Records;
            if (rawRecords.Count > 0)
            {
                videos = CreateVideosFromRawRecords(rawRecords);
                hasCache = !AreLibraryPathsChanged(folderPaths, scope.Roots);

                if (hasCache)
                {
                    cachedVideos = new List<MediaViewModel>(videos);
                    try
                    {
                        libraryChangeTracker!.Enable();
                        changeReader = libraryChangeTracker.GetChangeReader();
                        hasCache = await TryResolveLibraryChangeAsync(videos, changeReader);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to resolve video library changes from the change tracker.");
                    }
                }
            }
        }

        if (!hasCache)
        {
            videos = new List<MediaViewModel>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await BatchFetchVideosAsync(scope.Queries, videos, visited, cancellationToken, progress);

            if (SearchRemovableStorage && await KnownFolderAccess.HasAccessAsync(KnownFolderId.RemovableDevices))
            {
                IReadOnlyList<StorageFileQueryResult> removableQueries = [CreateRemovableStorageVideosQuery()];
                await BatchFetchVideosAsync(removableQueries, videos, visited, cancellationToken, progress);
            }
        }
        else
        {
            foreach (MediaViewModel video in videos)
            {
                video.IsFromLibrary = true;
            }
        }

        if (!hasCache || !videos.SequenceEqual(cachedVideos))
        {
            await CacheVideosAsync(scope.Roots, videos, cancellationToken);
        }

        if (hasCache && changeReader != null)
        {
            await changeReader.AcceptChangesAsync();
        }
        else
        {
            libraryChangeTracker?.Reset();
        }

        return new VideosLibrary(videos);
    }

    private static void DetachMediaRelationships(MediaViewModel media)
    {
        if (media.Album != null)
        {
            media.Album.RelatedSongs.Remove(media);
            media.Album = null;
        }

        foreach (ArtistViewModel artist in media.Artists)
        {
            artist.RelatedSongs.Remove(media);
        }

        media.Artists = Array.Empty<ArtistViewModel>();
    }

    /// <summary>Reads raw music metadata from the SQLite cache. Runs on a background thread.</summary>
    private async Task<RawCacheLoadResultDto> LoadRawMusicCacheAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await _databaseService.LoadLibraryCacheAsync(MediaPlaybackType.Music);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to load the music library cache from the database.");
            return new RawCacheLoadResultDto();
        }
    }

    /// <summary>Reads raw video metadata from the SQLite cache. Runs on a background thread.</summary>
    private async Task<RawCacheLoadResultDto> LoadRawVideoCacheAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await _databaseService.LoadLibraryCacheAsync(MediaPlaybackType.Video);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to load the video library cache from the database.");
            return new RawCacheLoadResultDto();
        }
    }

    /// <summary>
    /// Converts raw music records from the database into <see cref="MediaViewModel"/> instances.
    /// Must be called on the UI thread so that <see cref="MediaViewModelFactory"/> has safe access
    /// to the existing library context collections.
    /// </summary>
    private List<MediaViewModel> CreateSongsFromRawRecords(List<RawMediaRecordDto> records)
    {
        var result = new List<MediaViewModel>(records.Count);
        foreach (var record in records)
        {
            try
            {
                MediaViewModel media = _mediaFactory.GetOrCreate(record.Path);
                media.IsFromLibrary = true;
                if (!media.DetailsLoaded)
                {
                    if (!string.IsNullOrEmpty(record.Title))
                        media.Name = record.Title;

                    media.MediaInfo = new MediaInfo(new MusicInfo
                    {
                        Title = record.Title,
                        Artist = record.Artist,
                        Album = record.Album,
                        AlbumArtist = record.AlbumArtist,
                        Composers = record.Composers,
                        Genre = record.Genre,
                        TrackNumber = record.TrackNumber,
                        Year = record.Year,
                        Duration = record.Duration,
                        Bitrate = record.Bitrate,
                    });
                }

                if (record.DateAdded != default)
                {
                    media.DateAdded = record.DateAdded.ToLocalTime();
                }

                result.Add(media);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to create a music MediaViewModel for file type '{FileType}'.", Path.GetExtension(record.Path));
            }
        }

        return result;
    }

    /// <summary>
    /// Converts raw video records from the database into <see cref="MediaViewModel"/> instances.
    /// Must be called on the UI thread.
    /// </summary>
    private List<MediaViewModel> CreateVideosFromRawRecords(List<RawMediaRecordDto> records)
    {
        var result = new List<MediaViewModel>(records.Count);
        foreach (var record in records)
        {
            try
            {
                MediaViewModel media = _mediaFactory.GetOrCreate(record.Path);
                media.IsFromLibrary = true;
                if (!media.DetailsLoaded)
                {
                    if (!string.IsNullOrEmpty(record.Title))
                        media.Name = record.Title;

                    media.MediaInfo = new MediaInfo(new VideoInfo
                    {
                        Title = record.Title,
                        Subtitle = record.Subtitle,
                        Producers = record.Producers,
                        Writers = record.Writers,
                        Year = record.Year,
                        Duration = record.Duration,
                        Width = record.Width,
                        Height = record.Height,
                        Bitrate = record.VideoBitrate,
                    });
                }

                if (record.DateAdded != default)
                {
                    media.DateAdded = record.DateAdded.ToLocalTime();
                }

                result.Add(media);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to create a video MediaViewModel for file type '{FileType}'.", Path.GetExtension(record.Path));
            }
        }

        return result;
    }

    private async Task CacheSongsAsync(IReadOnlyList<StorageFolder> roots, List<MediaViewModel> songs, CancellationToken cancellationToken)
    {
        var folderPaths = roots.Select(f => f.Path).ToList();
        var snapshot = songs.Select(s => new MusicCacheRecordDto
        {
            Path = s.Location,
            Title = s.Name,
            DateAdded = s.DateAdded,
            Duration = s.MediaInfo.MusicProperties.Duration,
            Year = s.MediaInfo.MusicProperties.Year,
            Artist = s.MediaInfo.MusicProperties.Artist,
            Album = s.MediaInfo.MusicProperties.Album,
            AlbumArtist = s.MediaInfo.MusicProperties.AlbumArtist,
            Composers = s.MediaInfo.MusicProperties.Composers,
            Genre = s.MediaInfo.MusicProperties.Genre,
            TrackNumber = s.MediaInfo.MusicProperties.TrackNumber,
            Bitrate = s.MediaInfo.MusicProperties.Bitrate,
        }).ToList();

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _databaseService.SaveMusicCacheAsync(folderPaths, snapshot);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogError(e, "Failed to write the music library cache to the database.");
        }
    }

    private async Task CacheVideosAsync(IReadOnlyList<StorageFolder> roots, List<MediaViewModel> videos, CancellationToken cancellationToken)
    {
        var folderPaths = roots.Select(f => f.Path).ToList();
        var snapshot = videos.Select(v => new VideoCacheRecordDto
        {
            Path = v.Location,
            Title = v.Name,
            DateAdded = v.DateAdded,
            Duration = v.MediaInfo.VideoProperties.Duration,
            Year = v.MediaInfo.VideoProperties.Year,
            Subtitle = v.MediaInfo.VideoProperties.Subtitle,
            Producers = v.MediaInfo.VideoProperties.Producers,
            Writers = v.MediaInfo.VideoProperties.Writers,
            Width = v.MediaInfo.VideoProperties.Width,
            Height = v.MediaInfo.VideoProperties.Height,
            VideoBitrate = v.MediaInfo.VideoProperties.Bitrate,
        }).ToList();

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _databaseService.SaveVideoCacheAsync(folderPaths, snapshot);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogError(e, "Failed to write the video library cache to the database.");
        }
    }

    private async Task BatchFetchMusicAsync(
        IReadOnlyList<StorageFileQueryResult> queries,
        List<MediaViewModel> target,
        HashSet<string> visited,
        AlbumViewModelFactory albumFactory,
        ArtistViewModelFactory artistFactory,
        CancellationToken cancellationToken,
        IProgress<MusicLibrary>? progress)
    {
        DateTime lastReport = DateTime.MinValue;
        foreach (StorageFileQueryResult query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Paging is per query: each one has its own result set, so the batch index cannot be
            // derived from the number of songs collected so far.
            uint fetchIndex = 0;
            while (true)
            {
                IReadOnlyList<StorageFile> files = await FetchFilesFromStorage(query, fetchIndex);
                if (files.Count == 0) break;
                fetchIndex += (uint)files.Count;

                foreach (StorageFile file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!visited.Add(file.Path)) continue;

                    MediaViewModel song = _mediaFactory.Create(file);
                    song.IsFromLibrary = true;
                    await song.LoadDetailsAsync(_sourceMetadataService, cancellationToken);
                    albumFactory.AddSong(song);
                    artistFactory.AddSong(song);
                    song.Album = albumFactory.SongsToAlbums[song];
                    song.Artists = artistFactory.SongsToArtists[song].ToArray();
                    song.Album.UpdateProperties();
                    target.Add(song);
                }

                if (progress != null && (DateTime.UtcNow - lastReport).TotalSeconds >= 5)
                {
                    progress.Report(BuildMusicSnapshot(target, albumFactory, artistFactory));
                    lastReport = DateTime.UtcNow;
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private async Task BatchFetchVideosAsync(
        IReadOnlyList<StorageFileQueryResult> queries,
        List<MediaViewModel> target,
        HashSet<string> visited,
        CancellationToken cancellationToken,
        IProgress<VideosLibrary>? progress)
    {
        DateTime lastReport = DateTime.MinValue;
        foreach (StorageFileQueryResult query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            uint fetchIndex = 0;
            while (true)
            {
                IReadOnlyList<StorageFile> files = await FetchFilesFromStorage(query, fetchIndex);
                if (files.Count == 0) break;
                fetchIndex += (uint)files.Count;

                foreach (StorageFile file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!visited.Add(file.Path)) continue;

                    MediaViewModel video = _mediaFactory.Create(file);
                    video.IsFromLibrary = true;
                    await video.LoadDetailsAsync(_sourceMetadataService, cancellationToken);
                    target.Add(video);
                }

                if (progress != null && (DateTime.UtcNow - lastReport).TotalSeconds >= 5)
                {
                    progress.Report(new VideosLibrary(new List<MediaViewModel>(target)));
                    lastReport = DateTime.UtcNow;
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private static MusicLibrary BuildMusicSnapshot(
        List<MediaViewModel> songs,
        AlbumViewModelFactory albumFactory,
        ArtistViewModelFactory artistFactory)
    {
        return new MusicLibrary(
            new List<MediaViewModel>(songs),
            new Dictionary<string, AlbumViewModel>(albumFactory.Albums),
            new Dictionary<string, ArtistViewModel>(artistFactory.Artists),
            ExtractGenres(songs),
            albumFactory.UnknownAlbum,
            artistFactory.UnknownArtist);
    }

    private static IReadOnlyList<string> ExtractGenres(IEnumerable<MediaViewModel> songs)
    {
        return songs
            .SelectMany(s => GenreHelpers.SplitGenres(s.MediaInfo.MusicProperties.Genre))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(g => g, StringComparer.CurrentCulture)
            .ToList();
    }

    private async Task<IReadOnlyList<StorageFile>> FetchFilesFromStorage(StorageFileQueryResult queryResult, uint fetchIndex, uint batchSize = 50)
    {
        try
        {
            return await queryResult.GetFilesAsync(fetchIndex, batchSize);
        }
        catch (Exception e)
        {
            // System.Exception: The library, drive, or media pool is empty.
            if (e.HResult != unchecked((int)0x800710D2))
            {
                e.Data[nameof(fetchIndex)] = fetchIndex;
                e.Data[nameof(batchSize)] = batchSize;
                _logger.LogError(e, "Failed to fetch files from storage.");
            }

            return Array.Empty<StorageFile>();
        }
    }

    private Task<bool> TryResolveLibraryChangeAsync(List<MediaViewModel> items, StorageLibraryChangeReader changeReader)
    {
        if (ApiInformation.IsMethodPresent("Windows.Storage.StorageLibraryChangeReader",
                "GetLastChangeId"))
        {
            var changeId = changeReader.GetLastChangeId();
            if (changeId == StorageLibraryLastChangeId.Unknown)
            {
                return Task.FromResult(false);
            }

            if (changeId > 0)
            {
                return TryResolveLibraryBatchChangeAsync(items, changeReader);
            }
        }
        else
        {
            return TryResolveLibraryBatchChangeAsync(items, changeReader);
        }

        return Task.FromResult(true);
    }

    [DynamicWindowsRuntimeCast(typeof(StorageFile))]
    private async Task<bool> TryResolveLibraryBatchChangeAsync(List<MediaViewModel> items, StorageLibraryChangeReader changeReader)
    {
        var changeBatch = await changeReader.ReadBatchAsync();
        foreach (StorageLibraryChange change in changeBatch)
        {
            // If this is a folder change then give up
            if (change.IsOfType(StorageItemTypes.Folder) &&
                change.ChangeType is not (StorageLibraryChangeType.IndexingStatusChanged
                    or StorageLibraryChangeType.EncryptionChanged)) return false;

            StorageFile file;
            MediaViewModel? existing;
            switch (change.ChangeType)
            {
                case StorageLibraryChangeType.Created:
                case StorageLibraryChangeType.MovedIntoLibrary:
                    file = (StorageFile)await change.GetStorageItemAsync();
                    items.Add(_mediaFactory.GetOrCreate(file));
                    break;

                case StorageLibraryChangeType.Deleted:
                case StorageLibraryChangeType.MovedOutOfLibrary:
                    existing = items.Find(s =>
                        s.Location.Equals(change.PreviousPath, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        DetachMediaRelationships(existing);
                        items.Remove(existing);
                    }

                    break;

                case StorageLibraryChangeType.MovedOrRenamed:
                    file = (StorageFile)await change.GetStorageItemAsync();
                    existing = items.Find(s =>
                        s.Location.Equals(change.PreviousPath, StringComparison.OrdinalIgnoreCase));

                    var newMedia = _mediaFactory.GetOrCreate(file);
                    if (existing != null)
                    {
                        var existingInfo = existing.MediaInfo;
                        DetachMediaRelationships(existing);
                        items.Remove(existing);
                        newMedia.MediaInfo = existingInfo;
                    }

                    items.Add(newMedia);
                    break;

                case StorageLibraryChangeType.ContentsChanged:
                case StorageLibraryChangeType.ContentsReplaced:
                    file = (StorageFile)await change.GetStorageItemAsync();
                    existing = items.Find(s =>
                        s.Location.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
                    existing?.UpdateSource(file);
                    break;

                case StorageLibraryChangeType.EncryptionChanged:
                case StorageLibraryChangeType.IndexingStatusChanged:
                    break;
                case StorageLibraryChangeType.ChangeTrackingLost:
                    return false;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the folder set the cache was built from still matches the folders that are scanned now.
    /// </summary>
    private static bool AreLibraryPathsChanged(IReadOnlyCollection<string> cachedFolderPaths, IReadOnlyList<StorageFolder> roots)
    {
        var paths = roots.Select(f => f.Path).ToList();
        if (cachedFolderPaths.Count != paths.Count) return true;
        return cachedFolderPaths.Any(cachedPath => !paths.Contains(cachedPath, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether the user added folders that are not part of the Windows library definition.
    /// </summary>
    /// <remarks>
    /// The change tracker only reports changes inside the library it belongs to, so a cache built
    /// over hand-added folders cannot be kept up to date with it.
    /// </remarks>
    private async Task<bool> HasExtraRootsAsync(MediaPlaybackType mediaType)
    {
        IReadOnlyList<string> extras = await LoadExtraRootPathsAsync(mediaType);
        return extras.Count > 0;
    }

    private static StorageFileQueryResult CreateRemovableStorageMusicQuery()
    {
        // Removable storage does not support any other default queries.
        // Other than Default and SortByName, all other queries return empty results.
        QueryOptions queryOptions = new(CommonFileQuery.OrderByName, FilesHelpers.SupportedAudioFormats.ToArray());
        queryOptions.SetPropertyPrefetch(
            PropertyPrefetchOptions.BasicProperties | PropertyPrefetchOptions.MusicProperties,
            CustomPropertyKeys);
        return KnownFolders.RemovableDevices.CreateFileQueryWithOptions(queryOptions);
    }

    private static StorageFileQueryResult CreateRemovableStorageVideosQuery()
    {
        // Removable storage does not support any other default queries.
        // Other than Default and SortByName, all other queries return empty results.
        QueryOptions queryOptions = new(CommonFileQuery.OrderByName, FilesHelpers.SupportedVideoFormats.ToArray());
        queryOptions.SetPropertyPrefetch(
            PropertyPrefetchOptions.BasicProperties | PropertyPrefetchOptions.VideoProperties,
            CustomPropertyKeys);
        return KnownFolders.RemovableDevices.CreateFileQueryWithOptions(queryOptions);
    }
}

