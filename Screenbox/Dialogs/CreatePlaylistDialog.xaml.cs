using System;
using System.Threading.Tasks;
using Screenbox.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Screenbox.Core.Helpers;

// The Content Dialog item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Screenbox.Dialogs;

public sealed partial class CreatePlaylistDialog : ContentDialog
{
    private const int MaxPlaylistNameLength = 100;

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    public CreatePlaylistDialog()
    {
        this.DefaultStyleKey = typeof(ContentDialog);
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)WindowAccessor.Current.Content).RequestedTheme;
    }

    public static async Task<string?> GetPlaylistNameAsync()
    {
        CreatePlaylistDialog dialog = new();
        ContentDialogResult result = await DialogHelper.ShowAsync(dialog);
        string playlistName = dialog.PlaylistNameTextBox.Text.Trim();
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
