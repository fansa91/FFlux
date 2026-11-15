using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Screenbox.Core.Helpers;
using Screenbox.Helpers;

namespace Screenbox.Dialogs;

/// <summary>
/// Renames one saved stream. Shares the shape of <see cref="RenamePlaylistDialog"/> so the
/// keyboard flow (F2, name pre-selected, Enter confirms) matches the rest of the app.
/// </summary>
public sealed partial class RenameStreamDialog : ContentDialog
{
    private const int MaxNameLength = 200;

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    public RenameStreamDialog(string currentName)
    {
        this.DefaultStyleKey = typeof(ContentDialog);
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)WindowAccessor.Current.Content).RequestedTheme;

        // ContentDialog exposes a Resources property, which hides the generated
        // Screenbox.Strings.Resources class, so the type has to be fully qualified here.
        Title = Screenbox.Strings.Resources.NetworkStreamsRenameTitle;
        NameBox.Text = currentName ?? string.Empty;
        NameBox.SelectAll();
    }

    /// <summary>
    /// Shows the dialog and returns the new name, or <see langword="null"/> when cancelled.
    /// </summary>
    public async Task<string?> GetNameAsync()
    {
        ContentDialogResult result = await DialogHelper.ShowAsync(this);
        string name = NameBox.Text?.Trim() ?? string.Empty;
        return result == ContentDialogResult.Primary && name.Length > 0 ? name : null;
    }

    private void NameBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        string text = NameBox.Text?.Trim() ?? string.Empty;
        IsPrimaryButtonEnabled = text.Length > 0 && text.Length <= MaxNameLength;
    }
}
