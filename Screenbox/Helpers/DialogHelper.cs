using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Screenbox.Core.Helpers;

namespace Screenbox.Helpers;

/// <summary>
/// Shows a <see cref="ContentDialog"/> with an explicit XamlRoot.
/// <para>
/// UWP resolved the dialog host from <c>Window.Current</c>. WinUI 3 desktop has no
/// implicit host, so <c>ShowAsync()</c> throws 0x80070578 / "This element is not
/// associated with a XamlRoot" unless the root frame's XamlRoot is assigned first.
/// </para>
/// </summary>
public static class DialogHelper
{
    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (dialog.XamlRoot is null)
        {
            try
            {
                if (WindowAccessor.CurrentOrNull?.Content is FrameworkElement { XamlRoot: { } xamlRoot })
                {
                    dialog.XamlRoot = xamlRoot;
                }
            }
            catch (Exception)
            {
                // Let ShowAsync report the real failure if no root can be resolved.
            }
        }

        return await dialog.ShowAsync();
    }
}
