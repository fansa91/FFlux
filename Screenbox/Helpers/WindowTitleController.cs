using System.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.ViewModels;

namespace Screenbox.Helpers;

/// <summary>
/// Keeps the window caption in sync with the play queue.
/// <para>
/// A packaged app set its caption through <c>ApplicationView.Title</c>. An unpackaged
/// WinUI 3 window has no <c>ApplicationView</c>, so the caption fell back to the
/// framework default ("WinUI Desktop") for the whole session. <see cref="Window.Title"/>
/// is the supported replacement and is what Task View, Alt+Tab and the taskbar tooltip read.
/// </para>
/// </summary>
internal sealed class WindowTitleController
{
    private const string AppName = "Screenbox";

    private readonly Window _window;
    private MediaViewModel? _current;

    public WindowTitleController(Window window)
    {
        _window = window;
    }

    /// <summary>
    /// Applies the default caption and starts tracking queue changes.
    /// Call once, after the window exists.
    /// </summary>
    public void Attach()
    {
        Apply(AppName);
        WeakReferenceMessenger.Default.Register<QueueCurrentItemChangedMessage>(
            this, static (recipient, message) =>
                ((WindowTitleController)recipient).OnCurrentItemChanged(message.Value));
    }

    private void OnCurrentItemChanged(MediaViewModel? media)
    {
        // Queue changes are raised from background metadata work as well as from the
        // player page, so normalise to the UI thread before touching the window.
        AppDispatcher.Current.TryExecuteOrEnqueue(() =>
        {
            if (_current is not null)
            {
                _current.PropertyChanged -= OnCurrentMediaPropertyChanged;
            }

            _current = media;

            if (_current is not null)
            {
                _current.PropertyChanged += OnCurrentMediaPropertyChanged;
            }

            Update();
        });
    }

    private void OnCurrentMediaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Metadata parsing replaces the placeholder file name with the real title.
        if (e.PropertyName == nameof(MediaViewModel.Name))
        {
            AppDispatcher.Current.TryExecuteOrEnqueue(Update);
        }
    }

    private void Update()
    {
        string name = _current?.Name ?? string.Empty;
        Apply(string.IsNullOrWhiteSpace(name) ? AppName : $"{name} - {AppName}");
    }

    private void Apply(string title)
    {
        try
        {
            _window.Title = title;
        }
        catch
        {
            // The window can already be gone during shutdown; a stale caption is harmless.
        }
    }
}
