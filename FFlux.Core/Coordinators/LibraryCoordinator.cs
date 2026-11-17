using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Windows.Devices.Enumeration;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.System;

namespace Screenbox.Core.Coordinators;

/// <summary>
/// Stateful coordinator that owns library watchers/timers and invokes <see cref="ILibraryService"/> operations.
/// </summary>
public sealed partial class LibraryCoordinator : ILibraryCoordinator,
    IRecipient<SettingChangedMessage<LibrarySettingsContext, bool>>
{
    private readonly LibraryContext _context;
    private readonly ILibraryService _libraryService;
    private readonly LibrarySettingsContext _librarySettings;
    private readonly ILogger<LibraryCoordinator> _logger;

    private readonly IAppDispatcherTimer _musicRefreshTimer;
    private readonly IAppDispatcherTimer _videosRefreshTimer;
    private readonly IAppDispatcherTimer _storageDeviceRefreshTimer;

    private readonly DeviceWatcher? _portableStorageDeviceWatcher;

    private IReadOnlyList<StorageFolder> _musicRoots = Array.Empty<StorageFolder>();
    private IReadOnlyList<StorageFolder> _videosRoots = Array.Empty<StorageFolder>();
    private List<StorageFileQueryResult> _musicQueries = new();
    private List<StorageFileQueryResult> _videosQueries = new();
    private CancellationTokenSource? _musicFetchCts;
    private CancellationTokenSource? _videosFetchCts;

    private bool UseIndexer => _librarySettings.UseIndexer;
    private bool SearchRemovableStorage => _librarySettings.SearchRemovableStorage && SystemInformation.IsXbox;

    public LibraryCoordinator(
        LibraryContext context,
        ILibraryService libraryService,
        LibrarySettingsContext librarySettings,
        ILogger<LibraryCoordinator> logger)
    {
        _context = context;
        _libraryService = libraryService;
        _librarySettings = librarySettings;
        _logger = logger;

        IAppDispatcher dispatcherQueue = AppDispatcher.Current;
        _musicRefreshTimer = dispatcherQueue.CreateTimer();
        _videosRefreshTimer = dispatcherQueue.CreateTimer();
        _storageDeviceRefreshTimer = dispatcherQueue.CreateTimer();

        WeakReferenceMessenger.Default.Register<SettingChangedMessage<LibrarySettingsContext, bool>>(this);

        if (SystemInformation.IsXbox)
        {
            _portableStorageDeviceWatcher = DeviceInformation.CreateWatcher(DeviceClass.PortableStorageDevice);
            _portableStorageDeviceWatcher.Removed += OnPortableStorageDeviceChanged;
            _portableStorageDeviceWatcher.Updated += OnPortableStorageDeviceChanged;
        }
    }

    public void Receive(SettingChangedMessage<LibrarySettingsContext, bool> message) =>
        FireAndForget.Run(
            () => HandleSettingChangedAsync(message),
            "LibraryCoordinator.Receive(SettingChangedMessage)");

    private async Task HandleSettingChangedAsync(SettingChangedMessage<LibrarySettingsContext, bool> message)
    {
        if (message.PropertyName is nameof(LibrarySettingsContext.UseIndexer) or nameof(LibrarySettingsContext.SearchRemovableStorage))
        {
            await RefreshWatchersAsync();
        }
    }

    /// <inheritdoc/>
    public async Task EnsureWatchingAsync()
    {
        await EnsureWatchingMusicAsync();
        await EnsureWatchingVideosAsync();

        if (SearchRemovableStorage)
        {
            StartPortableStorageDeviceWatcher();
        }
    }

    /// <inheritdoc/>
    public async Task RefreshWatchersAsync()
    {
        StopWatching();
        await EnsureWatchingAsync();
    }

    /// <inheritdoc/>
    public async Task FetchMusicAsync(bool useCache = true)
    {
        if (_context.MusicStorageLibrary is null || _musicQueries.Count == 0) return;
        _musicFetchCts?.Cancel();
        using CancellationTokenSource cts = new();
        _musicFetchCts = cts;
        _context.IsLoadingMusic = true;
        try
        {
            var progress = new Progress<MusicLibrary>(report =>
            {
                // A snapshot posted while this fetch was still running can be delivered after it
                // finished - and after a later fetch replaced the library - so stale reports are
                // dropped instead of overwriting the final result.
                if (!ReferenceEquals(_musicFetchCts, cts) || cts.Token.IsCancellationRequested) return;
                _context.Music = report;
            });
            LibraryScanScope scope = new(_context.MusicStorageLibrary, _musicRoots, _musicQueries);
            var result = await _libraryService.FetchMusicAsync(scope, useCache, cts.Token, progress);
            _context.IsLoadingMusic = false;
            _context.Music = result;
        }
        catch (OperationCanceledException)
        {
            // A newer fetch replaced this one. Logged so a "begin" without "end" is not mistaken
            // for a crash in the stage trace.
            StartupTrace.Write("scan.music.cancelled");
        }
        catch (Exception e)
        {
            // A scan failure (I/O, a bad container, a WinRT error) must not escape into a
            // fire-and-forget task or a XAML callback as a fatal exception. Keep the last known
            // library on screen and let the finally block clear the loading flag.
            StartupTrace.Write("scan.music.failed", e);
            _logger.LogError(e, "Failed to fetch the music library; keeping the previous snapshot.");
        }
        finally
        {
            if (!cts.Token.IsCancellationRequested)
            {
                _context.IsLoadingMusic = false;
            }

            _musicFetchCts = null;
        }
    }

    /// <inheritdoc/>
    public async Task FetchVideosAsync(bool useCache = true)
    {
        if (_context.VideosStorageLibrary is null || _videosQueries.Count == 0) return;
        _videosFetchCts?.Cancel();
        using CancellationTokenSource cts = new();
        _videosFetchCts = cts;
        _context.IsLoadingVideos = true;
        try
        {
            var progress = new Progress<VideosLibrary>(report =>
            {
                // See FetchMusicAsync: a late snapshot must not overwrite a newer result.
                if (!ReferenceEquals(_videosFetchCts, cts) || cts.Token.IsCancellationRequested) return;
                _context.Videos = report;
            });
            LibraryScanScope scope = new(_context.VideosStorageLibrary, _videosRoots, _videosQueries);
            var result = await _libraryService.FetchVideosAsync(scope, useCache, cts.Token, progress);
            _context.IsLoadingVideos = false;
            _context.Videos = result;
        }
        catch (OperationCanceledException)
        {
            StartupTrace.Write("scan.videos.cancelled");
        }
        catch (Exception e)
        {
            // Keep the last known library on screen; the finally block clears the loading flag.
            StartupTrace.Write("scan.videos.failed", e);
            _logger.LogError(e, "Failed to fetch the videos library; keeping the previous snapshot.");
        }
        finally
        {
            if (!cts.Token.IsCancellationRequested)
            {
                _context.IsLoadingVideos = false;
            }

            _videosFetchCts = null;
        }
    }

    public void Dispose()
    {
        StopWatching();

        if (_portableStorageDeviceWatcher is not null)
        {
            _portableStorageDeviceWatcher.Removed -= OnPortableStorageDeviceChanged;
            _portableStorageDeviceWatcher.Updated -= OnPortableStorageDeviceChanged;
        }
    }

    private void StopWatching()
    {
        DetachMusicQueries();
        DetachVideosQueries();

        if (_portableStorageDeviceWatcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            _portableStorageDeviceWatcher.Stop();
        }

        _musicRefreshTimer.Stop();
        _videosRefreshTimer.Stop();
        _storageDeviceRefreshTimer.Stop();
    }

    private void DetachMusicQueries()
    {
        foreach (StorageFileQueryResult query in _musicQueries)
        {
            query.ContentsChanged -= OnMusicQueryContentsChanged;
        }

        _musicQueries.Clear();
        _musicRoots = Array.Empty<StorageFolder>();
    }

    private void DetachVideosQueries()
    {
        foreach (StorageFileQueryResult query in _videosQueries)
        {
            query.ContentsChanged -= OnVideosQueryContentsChanged;
        }

        _videosQueries.Clear();
        _videosRoots = Array.Empty<StorageFolder>();
    }

    private async Task EnsureWatchingMusicAsync()
    {
        if (_context.MusicStorageLibrary is null)
        {
            _context.MusicStorageLibrary = await _libraryService.InitializeMusicLibraryAsync();
        }

        if (_musicQueries.Count > 0 && _musicQueries.Any(query => ShouldUpdateQuery(query, UseIndexer)))
        {
            DetachMusicQueries();
        }

        // Known-folder access is only requestable by a packaged app. The unpackaged build used to
        // be told "DeniedByUser", which stopped the query from ever being created and left the
        // music library permanently empty. KnownFolderAccess answers true when there is no
        // capability to ask for, so the queries are created and read the real folders instead.
        if (_musicQueries.Count == 0 && _context.MusicStorageLibrary is not null
            && await KnownFolderAccess.HasAccessAsync(KnownFolderId.MusicLibrary))
        {
            _musicRoots = await _libraryService.GetLibraryRootsAsync(_context.MusicStorageLibrary, MediaPlaybackType.Music);
            List<StorageFileQueryResult> queries =
                _libraryService.CreateMusicLibraryQueries(_musicRoots, UseIndexer).ToList();
            foreach (StorageFileQueryResult query in queries)
            {
                query.ContentsChanged += OnMusicQueryContentsChanged;
            }

            _musicQueries = queries;
        }
    }

    private async Task EnsureWatchingVideosAsync()
    {
        if (_context.VideosStorageLibrary is null)
        {
            _context.VideosStorageLibrary = await _libraryService.InitializeVideosLibraryAsync();
        }

        if (_videosQueries.Count > 0 && _videosQueries.Any(query => ShouldUpdateQuery(query, UseIndexer)))
        {
            DetachVideosQueries();
        }

        // See EnsureWatchingMusicAsync for why the access check is delegated.
        if (_videosQueries.Count == 0 && _context.VideosStorageLibrary is not null
            && await KnownFolderAccess.HasAccessAsync(KnownFolderId.VideosLibrary))
        {
            _videosRoots = await _libraryService.GetLibraryRootsAsync(_context.VideosStorageLibrary, MediaPlaybackType.Video);
            List<StorageFileQueryResult> queries =
                _libraryService.CreateVideosLibraryQueries(_videosRoots, UseIndexer).ToList();
            foreach (StorageFileQueryResult query in queries)
            {
                query.ContentsChanged += OnVideosQueryContentsChanged;
            }

            _videosQueries = queries;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> AddMusicFolderAsync(StorageFolder folder)
    {
        if (_context.MusicStorageLibrary is null) return false;
        bool added = await _libraryService.AddLibraryRootAsync(_context.MusicStorageLibrary, MediaPlaybackType.Music, folder);
        if (!added) return false;

        await RestartMusicWatchingAsync();
        await FetchMusicAsync(useCache: false);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveMusicFolderAsync(StorageFolder folder)
    {
        if (!await _libraryService.RemoveLibraryRootAsync(MediaPlaybackType.Music, folder)) return false;

        await RestartMusicWatchingAsync();
        await FetchMusicAsync(useCache: false);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> AddVideosFolderAsync(StorageFolder folder)
    {
        if (_context.VideosStorageLibrary is null) return false;
        bool added = await _libraryService.AddLibraryRootAsync(_context.VideosStorageLibrary, MediaPlaybackType.Video, folder);
        if (!added) return false;

        await RestartVideosWatchingAsync();
        await FetchVideosAsync(useCache: false);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveVideosFolderAsync(StorageFolder folder)
    {
        if (!await _libraryService.RemoveLibraryRootAsync(MediaPlaybackType.Video, folder)) return false;

        await RestartVideosWatchingAsync();
        await FetchVideosAsync(useCache: false);
        return true;
    }

    private async Task RestartMusicWatchingAsync()
    {
        DetachMusicQueries();
        await EnsureWatchingMusicAsync();
    }

    private async Task RestartVideosWatchingAsync()
    {
        DetachVideosQueries();
        await EnsureWatchingVideosAsync();
    }

    private void OnMusicQueryContentsChanged(object sender, object args)
    {
        _musicRefreshTimer.DebounceCore(
            () => FireAndForget.Run(() => FetchMusicAsync(), "LibraryCoordinator.OnMusicQueryContentsChanged"),
            TimeSpan.FromMilliseconds(1000));
    }

    private void OnVideosQueryContentsChanged(object sender, object args)
    {
        _videosRefreshTimer.DebounceCore(
            () => FireAndForget.Run(() => FetchVideosAsync(), "LibraryCoordinator.OnVideosQueryContentsChanged"),
            TimeSpan.FromMilliseconds(1000));
    }

    private void OnPortableStorageDeviceChanged(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        if (!SearchRemovableStorage) return;

        _storageDeviceRefreshTimer.DebounceCore(
            () => FireAndForget.Run(async () =>
            {
                await FetchVideosAsync();
                await FetchMusicAsync();
            }, "LibraryCoordinator.OnPortableStorageDeviceChanged"),
            TimeSpan.FromMilliseconds(1000));
    }

    private void StartPortableStorageDeviceWatcher()
    {
        if (_portableStorageDeviceWatcher?.Status is DeviceWatcherStatus.Created or DeviceWatcherStatus.Stopped)
        {
            _portableStorageDeviceWatcher.Start();
        }
    }

    private static bool ShouldUpdateQuery(IStorageQueryResultBase query, bool useIndexer)
    {
        QueryOptions options = query.GetCurrentQueryOptions();
        bool agree1 = !useIndexer && options.IndexerOption == IndexerOption.DoNotUseIndexer;
        bool agree2 = useIndexer && options.IndexerOption != IndexerOption.DoNotUseIndexer;
        return !agree1 && !agree2;
    }
}
