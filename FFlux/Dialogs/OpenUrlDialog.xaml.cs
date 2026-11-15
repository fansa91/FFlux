using System;
using System.Threading.Tasks;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Screenbox.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Screenbox.Dialogs;

/// <summary>
/// Collects a network URL plus the optional anti-hotlink fields used to open it.
/// </summary>
/// <remarks>
/// The URL box is pre-filled from the clipboard when it holds something that looks like a stream,
/// because the usual flow is "copy a link somewhere else, paste it here".
/// </remarks>
public sealed partial class OpenUrlDialog : ContentDialog
{
    /// <summary>
    /// Clipboard text is only auto-filled for these schemes. Anything else (a file path, a note,
    /// an ordinary web page address) is left alone so the box does not fill up with noise.
    /// </summary>
    private static readonly string[] s_acceptedSchemes =
    [
        "http://", "https://", "rtsp://", "rtmp://", "mms://",
    ];

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    public OpenUrlDialog()
    {
        this.DefaultStyleKey = typeof(ContentDialog);
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)WindowAccessor.Current.Content).RequestedTheme;
        Opened += OnOpened;
    }

    /// <summary>
    /// Shows the dialog and returns what the user entered, or <see langword="null"/> on cancel.
    /// </summary>
    public static async Task<OpenUrlRequest?> GetRequestAsync()
    {
        OpenUrlDialog dialog = new();
        ContentDialogResult result = await DialogHelper.ShowAsync(dialog);
        if (result != ContentDialogResult.Primary) return null;

        string url = dialog.UrlBox.Text?.Trim() ?? string.Empty;
        if (!StreamUrlDetector.TryNormalize(url, out _, out string normalized)) return null;

        return new OpenUrlRequest(
            normalized,
            dialog.UserAgentBox.Text?.Trim() ?? string.Empty,
            dialog.RefererBox.Text?.Trim() ?? string.Empty,
            dialog.HeadersBox.Text?.Trim() ?? string.Empty);
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;

            string text = (await content.GetTextAsync()).Trim();
            if (text.Length == 0 || !HasAcceptedScheme(text)) return;

            UrlBox.Text = text;
            UrlBox.SelectAll();
            UrlBox.Focus(FocusState.Programmatic);
        }
        catch (Exception)
        {
            // The clipboard can be held by another process; an empty box is a fine fallback.
        }
    }

    private static bool HasAcceptedScheme(string text)
    {
        foreach (string scheme in s_acceptedSchemes)
        {
            if (text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private void UrlBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = StreamUrlDetector.TryNormalize(UrlBox.Text, out _, out _);
    }
}
