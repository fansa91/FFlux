using System;
using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Behaviors;
using Windows.Foundation.Collections;
using Windows.System;
using Screenbox.Core.Helpers;
using Windows.UI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Screenbox.Behaviors;

internal sealed partial class AutoFocusBehavior : BehaviorBase<Control>
{
    private bool _focused;
    private bool _eventTriggered;
    private FrameworkElement? _deferredElement;
    private readonly IAppDispatcher _dispatcherQueue;
    private readonly IAppDispatcherTimer _timer;

    public AutoFocusBehavior()
    {
        _dispatcherQueue = AppDispatcher.Current;
        _timer = _dispatcherQueue.CreateTimer();
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        _focused = false;
        _eventTriggered = false;
        if (AssociatedObject is ListViewBase { Items: { Count: 0 } items })
        {
            items.VectorChanged += ItemsOnVectorChanged;
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        ResetDeferredElement();
        if (AssociatedObject is ListViewBase { Items: { } items })
        {
            items.VectorChanged -= ItemsOnVectorChanged;
        }
    }

    private void ItemsOnVectorChanged(IObservableVector<object> sender, IVectorChangedEventArgs args)
    {
        if (sender.Count == 0 || _eventTriggered) return;
        sender.VectorChanged -= ItemsOnVectorChanged;
        // Event may fire multiple times synchronously.
        // Handler is called multiple times even after unregister
        _eventTriggered = true;
        if (!_focused)
        {
            _timer.DebounceCore(() =>
            {
                if (AssociatedObject != null)
                {
                    _focused = AssociatedObject.Focus(FocusState.Programmatic);
                }
            }, TimeSpan.FromMilliseconds(80));
        }
    }

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    protected override void OnAssociatedObjectLoaded()
    {
        // CoreWindow.GetKeyState is UWP-only. InputKeyboardSource is the WinUI 3 equivalent
        // and works on the current UI thread without a CoreApplication view.
        bool triggered = false;
        try
        {
            triggered =
                Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Space).HasFlag(CoreVirtualKeyStates.Down) ||
                Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Enter).HasFlag(CoreVirtualKeyStates.Down) ||
                Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.GamepadA).HasFlag(CoreVirtualKeyStates.Down);
        }
        catch (Exception)
        {
            // Focus must still be applied if key state cannot be queried.
        }

        // If yes than wait until key up then focus
        if (triggered && FocusManager.GetFocusedElement() is FrameworkElement element)
        {
            ResetDeferredElement();
            element.PreviewKeyUp -= ElementOnPreviewKeyUp;
            element.PreviewKeyUp += ElementOnPreviewKeyUp;
            _deferredElement = element;
        }
        else
        {
            _focused = AssociatedObject.Focus(FocusState.Programmatic);
        }
    }

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    private void ElementOnPreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        FrameworkElement element = (FrameworkElement)sender;
        element.PreviewKeyUp -= ElementOnPreviewKeyUp;
        if (e.Key is VirtualKey.Space or VirtualKey.Enter or VirtualKey.GamepadA && AssociatedObject != null)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _focused = AssociatedObject.Focus(FocusState.Programmatic);
            });
        }
    }

    private void ResetDeferredElement()
    {
        if (_deferredElement == null) return;
        _deferredElement.PreviewKeyUp -= ElementOnPreviewKeyUp;
        _deferredElement = null;
    }
}
