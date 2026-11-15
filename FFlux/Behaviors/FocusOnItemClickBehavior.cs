using CommunityToolkit.WinUI;
using Microsoft.Xaml.Interactivity;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Screenbox.Behaviors;

internal sealed partial class FocusOnItemClickBehavior : Behavior<ListViewBase>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.ItemClick += OnItemClick;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        AssociatedObject.ItemClick -= OnItemClick;
    }

    [DynamicWindowsRuntimeCast(typeof(SelectorItem))]
    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        SelectorItem? item = (SelectorItem?)AssociatedObject.ContainerFromItem(e.ClickedItem);
        item?.FindDescendant<ButtonBase>()?.Focus(FocusState.Programmatic);
    }
}
