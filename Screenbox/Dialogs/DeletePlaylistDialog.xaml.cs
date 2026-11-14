using Screenbox.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Screenbox.Core.Helpers;

// The Content Dialog item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Screenbox.Dialogs;

public sealed partial class DeletePlaylistDialog : ContentDialog
{
    private string PlaylistName { get; }

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    public DeletePlaylistDialog(string playlistName)
    {
        this.DefaultStyleKey = typeof(ContentDialog);
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)WindowAccessor.Current.Content).RequestedTheme;
        PlaylistName = playlistName;
    }
}
