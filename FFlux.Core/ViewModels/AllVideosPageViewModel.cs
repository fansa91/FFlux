using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.WinUI;
using Screenbox.Core.Contexts;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Windows.System;

namespace Screenbox.Core.ViewModels;

public sealed partial class AllVideosPageViewModel : ObservableRecipient,
    IRecipient<PropertyChangedMessage<VideosLibrary>>
{
    [ObservableProperty] public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial MediaViewModel? ContextMedia { get; set; }

    public ObservableCollection<MediaViewModel> Videos { get; }

    private readonly LibraryContext _libraryContext;
    private readonly IAppDispatcher _dispatcherQueue;
    private readonly IAppDispatcherTimer _timer;

    public AllVideosPageViewModel(LibraryContext libraryContext)
    {
        _libraryContext = libraryContext;
        _dispatcherQueue = AppDispatcher.Current;
        _timer = _dispatcherQueue.CreateTimer();
        Videos = new ObservableCollection<MediaViewModel>();

        Messenger.Register<PropertyChangedMessage<VideosLibrary>>(this);
    }

    public void Receive(PropertyChangedMessage<VideosLibrary> message)
    {
        _dispatcherQueue.TryExecuteGuarded(UpdateVideos);
    }

    public void UpdateVideos()
    {
        IsLoading = _libraryContext.IsLoadingVideos;
        IReadOnlyList<MediaViewModel> videos = _libraryContext.Videos.Videos;
        if (videos.Count < 5000)
        {
            // Only sync when the number of items is low enough
            // Sync on too many items can cause UI hang
            Videos.SyncItems(videos);
        }
        else
        {
            Videos.Clear();
            foreach (MediaViewModel video in videos)
            {
                Videos.Add(video);
            }
        }

        // Progressively update when it's still loading
        if (IsLoading)
        {
            _timer.DebounceCore(UpdateVideos, TimeSpan.FromSeconds(5));
        }
        else
        {
            _timer.Stop();
        }
    }

    [RelayCommand]
    private void Play(MediaViewModel media)
    {
        if (Videos.Count == 0) return;
        Messenger.SendQueueAndPlay(media, Videos, true, QueueOrigin.ForLibraryQueue(media));
    }
}
