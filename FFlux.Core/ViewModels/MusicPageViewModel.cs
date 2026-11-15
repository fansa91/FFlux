using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Screenbox.Core.Contexts;
using Screenbox.Core.Coordinators;
using Screenbox.Core.Enums;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Windows.Storage;
using Windows.System;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.ViewModels;

public sealed partial class MusicPageViewModel : ObservableRecipient,
    IRecipient<PropertyChangedMessage<MusicLibrary>>
{
    [ObservableProperty] public partial bool HasContent { get; set; }

    /// <summary>Gets a value indicating whether the Music library is available, used to enable the add-folder command.</summary>
    public bool LibraryLoaded => _libraryContext.MusicStorageLibrary != null;

    private readonly LibraryContext _libraryContext;
    private readonly IFilesService _filesService;
    private readonly ILibraryCoordinator _libraryCoordinator;
    private readonly IAppDispatcher _dispatcherQueue;

    public MusicPageViewModel(
        LibraryContext libraryContext,
        IFilesService filesService,
        ILibraryCoordinator libraryCoordinator)
    {
        _libraryContext = libraryContext;
        _filesService = filesService;
        _libraryCoordinator = libraryCoordinator;
        _dispatcherQueue = AppDispatcher.Current;
        HasContent = true;

        Messenger.Register<PropertyChangedMessage<MusicLibrary>>(this);
    }

    public void Receive(PropertyChangedMessage<MusicLibrary> message)
    {
        _dispatcherQueue.TryEnqueue(UpdateSongs);
    }

    public void UpdateSongs()
    {
        HasContent = _libraryContext.Music.Songs.Count > 0 || _libraryContext.IsLoadingMusic;
        AddFolderCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Asks for a folder to add to the Music library.
    /// </summary>
    /// <remarks>
    /// <c>StorageLibrary.RequestAddFolderAsync</c> needs package identity, so it answers nothing at
    /// all in this unpackaged build. The folder is picked here instead and remembered in the
    /// database, which is then scanned alongside the folders Windows knows about.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(LibraryLoaded))]
    private async Task AddFolderAsync()
    {
        try
        {
            StorageFolder? folder = await _filesService.PickFolderAsync();
            if (folder is null) return;

            await _libraryCoordinator.AddMusicFolderAsync(folder);
        }
        catch (Exception e)
        {
            Messenger.Send(new NotificationMessage(NotificationLevel.Error, NotificationKind.FolderAddFailed, message: e.Message));
        }
    }

}

