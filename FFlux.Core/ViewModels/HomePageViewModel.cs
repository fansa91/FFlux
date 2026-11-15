using Screenbox.Core.MediaSources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Microsoft.Extensions.Logging;
using CommunityToolkit.WinUI;
using Screenbox.Core.Contexts;
using Screenbox.Core.Factories;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Windows.Storage;
using Windows.System;

namespace Screenbox.Core.ViewModels;

public sealed partial class HomePageViewModel : ObservableRecipient,
    IRecipient<QueueCurrentItemChangedMessage>
{
    public ObservableCollection<MediaViewModel> Recent => _recentContext.Recent;

    public SelectionViewModel Selection { get; }

    /// <summary>
    /// How many entries the home page shows. The database keeps more, see
    /// <c>DatabaseService.MaxStoredRecentItems</c>.
    /// </summary>
    private const int MaxRecentItems = 50;

    [ObservableProperty]
    public partial MediaViewModel? ContextMedia { get; set; }

    private readonly RecentContext _recentContext;
    private readonly MediaViewModelFactory _mediaFactory;
    private readonly IFilesService _filesService;
    private readonly ISourceMetadataService _sourceMetadataService;
    private readonly GeneralSettingsContext _generalSettings;
    private readonly IQueueService _queueService;
    private readonly IAppDispatcher _dispatcherQueue;
    private readonly IAppDispatcherTimer _changeDebounceTimer;
    private readonly ILogger<HomePageViewModel> _logger;

    public HomePageViewModel(
        RecentContext recentContext,
        SelectionViewModel selection,
        MediaViewModelFactory mediaFactory,
        IFilesService filesService,
        ISourceMetadataService sourceMetadataService,
        GeneralSettingsContext generalSettings,
        IQueueService queueService,
        ILogger<HomePageViewModel> logger)
    {
        Selection = selection;
        _recentContext = recentContext;
        _mediaFactory = mediaFactory;
        _filesService = filesService;
        _sourceMetadataService = sourceMetadataService;
        _generalSettings = generalSettings;
        _queueService = queueService;
        _logger = logger;
        _dispatcherQueue = AppDispatcher.Current;
        _changeDebounceTimer = _dispatcherQueue.CreateTimer();

        Selection.SetItemsSource(Recent);
        Selection.PropertyChanged += Selection_OnPropertyChanged;

        Messenger.Register<QueueCurrentItemChangedMessage>(this);
    }

    public void Receive(QueueCurrentItemChangedMessage message)
    {
        if (_generalSettings.ShowRecent)
        {
            _changeDebounceTimer.DebounceCore(DebouncedAction, TimeSpan.FromMilliseconds(100));

            async void DebouncedAction()
            {
                await UpdateRecentMediaAsync(false).ConfigureAwait(false);
            }
        }
    }

    public async void OnLoaded()
    {
        // async void exceptions are posted back to the WinUI SynchronizationContext,
        // where CoreMessaging turns them into a process-killing stowed exception
        // (0xC000027B) instead of routing them to Application.UnhandledException.
        // Keep the home page best-effort.
        try
        {
            await UpdateContentAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to load home page content.");
        }
    }

    private void Selection_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionViewModel.IsAllSelected))
        {
            PlaySelectedCommand.NotifyCanExecuteChanged();
            PlaySelectedNextCommand.NotifyCanExecuteChanged();
            AddSelectedToQueueCommand.NotifyCanExecuteChanged();
            RemoveSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void OpenUrl(OpenUrlRequest? request)
    {
        StreamPlaybackHelper.Play(Messenger, _mediaFactory, request);
    }

    private async Task UpdateContentAsync()
    {
        // Update recent media
        if (_generalSettings.ShowRecent)
        {
            await UpdateRecentMediaAsync(true);
        }
        else
        {
            lock (Recent)
            {
                Recent.Clear();
                _recentContext.PathToMediaMappings.Clear();
                _recentContext.IsLoaded = true;
            }
        }
    }

    private async Task UpdateRecentMediaAsync(bool loadMediaDetails)
    {
        // The list is backed by the app database. It used to come from the Windows MRU list, which
        // is a package-identity API: reading it throws 0x80070057 in this unpackaged build, so the
        // list was always empty no matter how much was played.
        IReadOnlyList<StorageFile> files;
        try
        {
            files = await _filesService.GetRecentFilesAsync(MaxRecentItems).ConfigureAwait(true);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to load the recent list.");
            return;
        }

        var targetList = new List<MediaViewModel>(files.Count);
        var mappings = new Dictionary<string, MediaViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (StorageFile file in files)
        {
            // TODO: Add support for playing playlist file from home page
            if (file.IsSupportedPlaylist())
            {
                continue;
            }

            MediaViewModel media = ResolveRecentMedia(file);
            targetList.Add(media);
            if (!string.IsNullOrEmpty(file.Path))
            {
                mappings[file.Path] = media;
            }
        }

        lock (Recent)
        {
            _recentContext.PathToMediaMappings.Clear();
            foreach (KeyValuePair<string, MediaViewModel> pair in mappings)
            {
                _recentContext.PathToMediaMappings[pair.Key] = pair.Value;
            }

            // Sync the observable collection in-place
            Recent.SyncItems(targetList);
            _recentContext.IsLoaded = true;
        }

        // Load media details & thumbnails for any items that need them
        if (!loadMediaDetails) return;
        var loadingTasks = new List<Task>();
        foreach (MediaViewModel media in Recent)
        {
            if (!media.DetailsLoaded)
            {
                loadingTasks.Add(SafeLoadDetailsAsync(media));
            }

            if (media.Thumbnail == null)
            {
                loadingTasks.Add(SafeLoadThumbnailAsync(media));
            }
        }

        if (loadingTasks.Count > 0)
        {
            await Task.WhenAll(loadingTasks);
        }
    }

    /// <summary>
    /// Reuses the view model already shown for this path when there is one, so a refresh does not
    /// throw away loaded metadata and thumbnails.
    /// </summary>
    private MediaViewModel ResolveRecentMedia(StorageFile file)
    {
        string path = file.Path;
        if (!string.IsNullOrEmpty(path) &&
            _recentContext.PathToMediaMappings.TryGetValue(path, out MediaViewModel? cached) &&
            cached.Source is IFileMediaSource { StorageFile: { } cachedFile } &&
            string.Equals(cachedFile.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            return cached;
        }

        return _mediaFactory.GetOrCreate(file);
    }

    private async Task SafeLoadDetailsAsync(MediaViewModel media)
    {
        try
        {
            await media.LoadDetailsAsync(_sourceMetadataService);
        }
        catch (ArgumentException)
        {
            // Expected: the underlying StorageFile (e.g. from MRU) may be stale and
            // throw ArgumentException. Ignore silently — this is a known bad state.
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to load recent media details for file type '{FileType}'.", Path.GetExtension(media.Location));
        }
    }

    private async Task SafeLoadThumbnailAsync(MediaViewModel media)
    {
        try
        {
            await media.LoadThumbnailAsync();
        }
        catch (ArgumentException)
        {
            // Expected: the underlying StorageFile (e.g. from MRU) may be stale and
            // throw ArgumentException. Ignore silently — this is a known bad state.
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to load a thumbnail for file type '{FileType}'.", Path.GetExtension(media.Location));
        }
    }

    [RelayCommand]
    private void Play(MediaViewModel media)
    {
        if (media.IsMediaActive)
        {
            Messenger.Send(new TogglePlayPauseMessage(false));
        }
        else
        {
            Messenger.Send(new PlayMediaMessage(media, false));
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(MediaViewModel media)
    {
        lock (Recent)
        {
            Recent.Remove(media);
            if (!string.IsNullOrEmpty(media.Location))
            {
                _recentContext.PathToMediaMappings.Remove(media.Location);
            }
        }

        await _filesService.RemoveFromRecentAsync(media.Location);
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        StorageFolder? folder = await _filesService.PickFolderAsync();
        if (folder == null) return;
        IReadOnlyList<IStorageItem> items = await _filesService.GetSupportedItems(folder).GetItemsAsync();
        IStorageFile[] files = items.OfType<IStorageFile>().ToArray();
        if (files.Length == 0) return;
        await Messenger.PlayFilesAsync(files, _queueService, _generalSettings.EnqueueAllFilesInFolder);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void PlaySelected()
    {
        var items = Selection.GetSelectedItems<MediaViewModel>().ToArray();
        if (items.Length > 0)
        {
            Messenger.SendQueueAndPlay(items[0], items, pauseIfExists: false);
        }

        Selection.DisableSelectionMode();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void PlaySelectedNext()
    {
        var items = Selection.GetSelectedItems<MediaViewModel>();
        items.Reverse();
        Messenger.SendPlayNext(items.ToArray());
        Selection.DisableSelectionMode();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddSelectedToQueue()
    {
        var items = Selection.GetSelectedItems<MediaViewModel>().ToArray();
        Messenger.SendAddToQueue(items);
        Selection.DisableSelectionMode();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveSelectedAsync()
    {
        var copy = Selection.GetSelectedItems<MediaViewModel>().ToArray();
        foreach (MediaViewModel item in copy)
        {
            await RemoveAsync(item);
        }

        Selection.DisableSelectionMode();
    }

    private bool HasSelection() => Selection.SelectedRanges.Count > 0;
}
