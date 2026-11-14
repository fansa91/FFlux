using CommunityToolkit.WinUI.Helpers;
using Windows.UI.Core;
using Microsoft.UI.Xaml;
using Screenbox.Core.Helpers;

namespace Screenbox.Triggers;

/// <summary>
/// Applies visual states based on whether the host window is active.
/// <para>
/// The UWP implementation read <c>CoreWindow.ActivationMode</c> and subscribed to
/// <c>CoreWindow.Activated</c>. Neither exists for an unpackaged WinUI 3 desktop
/// window, so the trigger now follows <c>Window.Activated</c> and keeps the original
/// <see cref="CoreWindowActivationMode"/> values for XAML compatibility.
/// </para>
/// </summary>
public sealed partial class WindowActivationModeTrigger : StateTriggerBase
{
    private Window? _window;
    private bool _deactivated;

    /// <summary>Identifies the <see cref="ActivationMode"/> dependency property.</summary>
    public static readonly DependencyProperty ActivationModeProperty = DependencyProperty.Register(
        nameof(ActivationMode),
        typeof(CoreWindowActivationMode),
        typeof(WindowActivationModeTrigger),
        new PropertyMetadata(CoreWindowActivationMode.None, (d, e) => ((WindowActivationModeTrigger)d).UpdateTrigger()));

    /// <summary>Gets or sets the activation mode at which the visual state is applied.</summary>
    public CoreWindowActivationMode ActivationMode
    {
        [DynamicWindowsRuntimeCast(typeof(CoreWindowActivationMode))]
        get { return (CoreWindowActivationMode)GetValue(ActivationModeProperty); }
        set { SetValue(ActivationModeProperty, value); }
    }

    public WindowActivationModeTrigger()
    {
        WindowAccessor.WindowRegistered += OnWindowRegistered;
        if (WindowAccessor.CurrentOrNull is { } window) OnWindowRegistered(window);
        UpdateTrigger();
    }

    private void OnWindowRegistered(Window window)
    {
        if (_window is not null) _window.Activated -= Window_OnActivated;
        _window = window;
        window.Activated += Window_OnActivated;
        UpdateTrigger();
    }

    private void Window_OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        _deactivated = args.WindowActivationState == WindowActivationState.Deactivated;
        UpdateTrigger();
    }

    private void UpdateTrigger()
    {
        if (ActivationMode == CoreWindowActivationMode.None)
        {
            SetActive(false);
            return;
        }

        SetActive(ActivationMode == CoreWindowActivationMode.Deactivated
            ? _deactivated
            : !_deactivated);
    }
}
