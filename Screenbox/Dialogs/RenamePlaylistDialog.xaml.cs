using System;
using System.Threading.Tasks;
using Screenbox.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Screenbox.Core.Helpers;

namespace Screenbox.Dialogs;

public sealed partial class RenamePlaylistDialog : ContentDialog
{
    private const int MaxPlaylistNameLength = 100;

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    public RenamePlaylistDialog(string currentName)
    {
        this.DefaultStyleKey = typeof(ContentDialog);
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)WindowAccessor.Current.Content).RequestedTheme;
        PlaylistNameTextBox.Text = currentName;
        PlaylistNameTextBox.SelectAll();
    }

    public async Task<string?> GetPlaylistNameAsync()
    {
        ContentDialogResult result = await ShowAsync();
        string playlistName = PlaylistNameTextBox.Text.Trim();
        return result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(playlistName)
            ? playlistName
            : null;
    }

    private void PlaylistNameTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        string text = PlaylistNameTextBox.Text.Trim();
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(text) && text.Length <= MaxPlaylistNameLength;
    }
}
