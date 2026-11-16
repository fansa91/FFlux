using Microsoft.Xaml.Interactivity;
using Screenbox.Core.Helpers;
using Screenbox.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Screenbox.Behaviors;

internal sealed partial class ThumbnailGridViewBehavior : Behavior<GridView>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.ContainerContentChanging += OnContainerContentChanging;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        AssociatedObject.ContainerContentChanging -= OnContainerContentChanging;
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Phase != 0) return;
        // Caption and thumbnail loading hits storage, shell and FFmpeg; failures must stay
        // observable instead of escaping this XAML callback as a fatal stowed exception
        // (0xC000027B) that kills the process without a managed stack.
        switch (args.Item)
        {
            case AlbumViewModel album:
                FireAndForget.Run(() => album.LoadAlbumArtAsync(),
                    "ThumbnailGridViewBehavior.OnContainerContentChanging");
                break;
            case MediaViewModel media:
                FireAndForget.Run(() => media.LoadThumbnailAsync(),
                    "ThumbnailGridViewBehavior.OnContainerContentChanging");
                break;
            case StorageItemViewModel storageItem:
                FireAndForget.Run(async () =>
                    {
                        await storageItem.UpdateCaptionAsync();
                        if (storageItem.Media != null)
                        {
                            await storageItem.Media.LoadThumbnailAsync();
                        }
                    },
                    "ThumbnailGridViewBehavior.OnContainerContentChanging");
                break;
        }
    }
}
