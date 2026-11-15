using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Contexts;
using Screenbox.Core.Coordinators;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Windows.Storage;
using Windows.System;
using Microsoft.UI.Xaml.Navigation;

namespace Screenbox.Core.ViewModels;

public sealed partial class VideosPageViewModel : ObservableRecipient,
    IRecipient<PropertyChangedMessage<VideosLibrary>>
{
    public ObservableCollection<string> Breadcrumbs { get; } = new();

    [ObservableProperty] public partial bool HasVideos { get; set; }

    /// <summary>Gets a value indicating whether the Videos library is available, used to enable the add-folder command.</summary>
    public bool HasLibrary => _libraryContext.VideosStorageLibrary != null;

    private readonly List<StorageFolder> _breadcrumbLocations = new();
    private readonly LibraryContext _libraryContext;
    private readonly IFilesService _filesService;
    private readonly ILibraryCoordinator _libraryCoordinator;
    private readonly IAppDispatcher _dispatcherQueue;
    private readonly ILogger<VideosPageViewModel> _logger;

    public VideosPageViewModel(
        LibraryContext libraryContext,
        IFilesService filesService,
        ILibraryCoordinator libraryCoordinator,
        ILogger<VideosPageViewModel> logger)
    {
        _libraryContext = libraryContext;
        _filesService = filesService;
        _libraryCoordinator = libraryCoordinator;
        _logger = logger;
        HasVideos = true;
        _dispatcherQueue = AppDispatcher.Current;

        Messenger.Register<PropertyChangedMessage<VideosLibrary>>(this);
    }

    public void Receive(PropertyChangedMessage<VideosLibrary> message)
    {
        _dispatcherQueue.TryEnqueue(UpdateVideos);
    }

    public void UpdateVideos()
    {
        if (Breadcrumbs.Count == 0 && TryGetFirstFolder(out StorageFolder firstFolder))
        {
            Breadcrumbs.Add(firstFolder.DisplayName);
            _breadcrumbLocations.Add(firstFolder);
        }

        HasVideos = _libraryContext.Videos.Videos.Count > 0;
        AddFolderCommand.NotifyCanExecuteChanged();
    }

    public void OnContentFrameNavigated(object sender, NavigationEventArgs e)
    {
        IReadOnlyList<StorageFolder>? crumbs = e.Parameter as IReadOnlyList<StorageFolder>;
        UpdateBreadcrumbs(crumbs);
    }

    private bool TryGetFirstFolder(out StorageFolder folder)
    {
        try
        {
            folder = SystemInformation.IsXbox ? KnownFolders.RemovableDevices : KnownFolders.VideosLibrary;
            return true;
        }
        catch (Exception e)
        {
            folder = StorageFolder.GetFolderFromPathAsync(AppPaths.TemporaryFolder).AsTask().GetAwaiter().GetResult();
            Messenger.Send(new NotificationMessage(NotificationLevel.Error, NotificationKind.None, message: e.Message));
            _logger.LogError(e, "Failed to resolve the first videos folder.");
            return false;
        }
    }

    private void UpdateBreadcrumbs(IReadOnlyList<StorageFolder>? crumbs)
    {
        Breadcrumbs.Clear();
        _breadcrumbLocations.Clear();
        if (crumbs == null)
        {
            if (TryGetFirstFolder(out StorageFolder firstFolder))
            {
                Breadcrumbs.Add(firstFolder.DisplayName);
                _breadcrumbLocations.Add(firstFolder);
            }
        }
        else
        {
            foreach (StorageFolder storageFolder in crumbs)
            {
                Breadcrumbs.Add(storageFolder.DisplayName);
                _breadcrumbLocations.Add(storageFolder);
            }
        }
    }

    /// <summary>
    /// Asks for a folder to add to the Videos library.
    /// </summary>
    /// <remarks>
    /// <c>StorageLibrary.RequestAddFolderAsync</c> needs package identity, so the folder is picked
    /// here instead and remembered in the database. See <see cref="MusicPageViewModel"/>.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasLibrary))]
    private async Task AddFolderAsync()
    {
        try
        {
            StorageFolder? folder = await _filesService.PickFolderAsync();
            if (folder is null) return;

            await _libraryCoordinator.AddVideosFolderAsync(folder);
        }
        catch (Exception e)
        {
            Messenger.Send(new NotificationMessage(NotificationLevel.Error, NotificationKind.FolderAddFailed, message: e.Message));
        }
    }

}
