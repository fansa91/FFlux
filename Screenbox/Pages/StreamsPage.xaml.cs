using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Screenbox.Core.Enums;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Screenbox.Core.ViewModels;
using Screenbox.Dialogs;
using Screenbox.Helpers;
using Windows.System;

namespace Screenbox.Pages;

/// <summary>
/// The network stream library: quick add, search, play, rename, edit and delete.
/// </summary>
/// <remarks>
/// Dialogs live in the page rather than the view model, matching the playlist pages, because
/// showing one needs a <c>XamlRoot</c>.
/// </remarks>
public sealed partial class StreamsPage : Page
{
    internal StreamsPageViewModel ViewModel => (StreamsPageViewModel)DataContext;

    internal CommonViewModel Common { get; }

    public StreamsPage()
    {
        this.InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<StreamsPageViewModel>();
        Common = Ioc.Default.GetRequiredService<CommonViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.SearchQuery = SearchBox.Text;
    }

    private void AddButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ = AddFromQuickAddAsync();
    }

    private void QuickAddBox_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;

        e.Handled = true;
        _ = AddFromQuickAddAsync();
    }

    private async Task AddFromQuickAddAsync()
    {
        QuickAddResult result = await ViewModel.QuickAddAsync();

        if (result.Error == "invalid-url")
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                NotificationLevel.Warning,
                NotificationKind.MediaLoadFailed,
                message: Screenbox.Strings.Resources.NetworkStreamsInvalidUrl));
            return;
        }

        if (result.Item is null) return;

        if (result.PlaylistEntries.Count > 1)
        {
            await PromptImportPlaylistAsync(result);
        }
    }

    /// <summary>
    /// Offers to import every channel of an extended M3U list that was just added.
    /// </summary>
    private async Task PromptImportPlaylistAsync(QuickAddResult result)
    {
        if (result.Item is null) return;

        var dialog = new ContentDialog
        {
            Title = Screenbox.Strings.Resources.NetworkStreamsPlaylistTitle,
            Content = Screenbox.Strings.Resources.NetworkStreamsPlaylistMessage(result.PlaylistEntries.Count),
            PrimaryButtonText = Screenbox.Strings.Resources.NetworkStreamsAdd,
            CloseButtonText = Screenbox.Strings.Resources.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        ContentDialogResult choice = await dialog.ShowAsync();
        if (choice != ContentDialogResult.Primary) return;

        int imported = ViewModel.ImportPlaylist(result.PlaylistEntries, result.Item);
        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            NotificationLevel.Success,
            NotificationKind.None,
            message: Screenbox.Strings.Resources.NetworkStreamsImported(imported)));
    }

    private void StreamsList_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StreamItem item)
        {
            ViewModel.Play(item);
        }
    }

    private void PlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Play(ResolveItem(sender));
    }

    private async void RenameButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RenameAsync(ResolveItem(sender));
    }

    private async void EditButton_OnClick(object sender, RoutedEventArgs e)
    {
        await EditAsync(ResolveItem(sender));
    }

    private async void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        await DeleteAsync(ResolveItem(sender));
    }

    private async void StreamsList_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        StreamItem? selected = StreamsList.SelectedItem as StreamItem;

        switch (e.Key)
        {
            case VirtualKey.F2:
                e.Handled = true;
                await RenameAsync(selected);
                break;
            case VirtualKey.Delete:
                e.Handled = true;
                await DeleteAsync(selected);
                break;
        }
    }

    private async Task RenameAsync(StreamItem? item)
    {
        if (item is null) return;

        var dialog = new RenameStreamDialog(item.Name);
        string? newName = await dialog.GetNameAsync();
        if (newName is null) return;

        ViewModel.Rename(item, newName);
    }

    private async Task EditAsync(StreamItem? item)
    {
        if (item is null) return;

        var dialog = new EditStreamDialog(item);
        StreamItem? edited = await dialog.GetResultAsync();
        if (edited is null) return;

        ViewModel.ApplyEdit(item, edited);

        // A blank format means "auto detect", which is also the state after the URL changes.
        if (edited.FormatType == StreamFormatType.Unknown)
        {
            await ViewModel.RefreshFormatAsync(item);
        }
    }

    private async Task DeleteAsync(StreamItem? item)
    {
        if (item is null) return;

        var dialog = new ContentDialog
        {
            Title = Screenbox.Strings.Resources.NetworkStreamsDeleteTitle,
            Content = $"{item.Name}\n\n{Screenbox.Strings.Resources.NetworkStreamsDeleteMessage}",
            PrimaryButtonText = Screenbox.Strings.Resources.Delete,
            CloseButtonText = Screenbox.Strings.Resources.Cancel,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        ContentDialogResult choice = await dialog.ShowAsync();
        if (choice != ContentDialogResult.Primary) return;

        ViewModel.Remove(item);
    }

    /// <summary>
    /// Resolves the <see cref="StreamItem"/> behind a per-row action button.
    /// </summary>
    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    private static StreamItem? ResolveItem(object sender)
    {
        return (sender as FrameworkElement)?.DataContext as StreamItem;
    }
}
