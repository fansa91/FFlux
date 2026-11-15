using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Screenbox.Helpers;

namespace Screenbox.Dialogs;

/// <summary>
/// Edits a saved stream: name, URL, format override and the anti-hotlink fields.
/// </summary>
/// <remarks>
/// The dialog works on a clone and only hands it back when the user confirms, so a cancelled
/// edit cannot leave a half-modified entry behind.
/// </remarks>
public sealed partial class EditStreamDialog : ContentDialog
{
    private readonly StreamItem _draft;

    /// <summary>
    /// The formats offered by the picker, in display order. The combo box shows plain strings
    /// because XAML cannot bind to a custom managed type without the custom property provider
    /// support that Native AOT strips away.
    /// </summary>
    private readonly List<StreamFormatType> _formatValues = [];

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    public EditStreamDialog(StreamItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        this.DefaultStyleKey = typeof(ContentDialog);
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)WindowAccessor.Current.Content).RequestedTheme;

        _draft = item.Clone();

        // ContentDialog exposes a Resources property, which hides the generated
        // Screenbox.Strings.Resources class, so the type has to be fully qualified here.
        Title = Screenbox.Strings.Resources.NetworkStreamsEditTitle;

        NameBox.Text = _draft.Name;
        UrlBox.Text = _draft.Url;
        UserAgentBox.Text = _draft.UserAgent;
        RefererBox.Text = _draft.Referer;
        HeadersBox.Text = _draft.Headers;

        List<string> labels = [Screenbox.Strings.Resources.NetworkStreamsFormatAuto];
        _formatValues.Add(StreamFormatType.Unknown);
        foreach (StreamFormatType format in Enum.GetValues<StreamFormatType>())
        {
            if (format == StreamFormatType.Unknown) continue;
            labels.Add(format.ToBadgeText());
            _formatValues.Add(format);
        }

        FormatBox.ItemsSource = labels;
        FormatBox.SelectedIndex = Math.Max(0, _formatValues.IndexOf(_draft.FormatType));

        IsPrimaryButtonEnabled = StreamUrlDetector.TryNormalize(_draft.Url, out _, out _);
    }

    /// <summary>
    /// Shows the dialog and returns the edited entry, or <see langword="null"/> when cancelled.
    /// </summary>
    public async Task<StreamItem?> GetResultAsync()
    {
        ContentDialogResult result = await DialogHelper.ShowAsync(this);
        if (result != ContentDialogResult.Primary) return null;

        _draft.Name = NameBox.Text?.Trim() ?? string.Empty;
        _draft.Url = UrlBox.Text?.Trim() ?? string.Empty;
        _draft.UserAgent = UserAgentBox.Text?.Trim() ?? string.Empty;
        _draft.Referer = RefererBox.Text?.Trim() ?? string.Empty;
        _draft.Headers = HeadersBox.Text?.Trim() ?? string.Empty;
        _draft.FormatType = FormatBox.SelectedIndex >= 0 && FormatBox.SelectedIndex < _formatValues.Count
            ? _formatValues[FormatBox.SelectedIndex]
            : StreamFormatType.Unknown;
        return _draft;
    }

    private void UrlBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = StreamUrlDetector.TryNormalize(UrlBox.Text, out _, out _);
    }
}
