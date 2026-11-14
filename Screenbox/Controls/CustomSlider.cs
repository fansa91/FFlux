using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Screenbox.Controls;

internal sealed partial class CustomSlider : Slider
{
    public static readonly DependencyProperty IsKeyDownEnabledProperty = DependencyProperty.Register(
        nameof(IsKeyDownEnabled), typeof(bool), typeof(CustomSlider), new PropertyMetadata(true));

    public bool IsKeyDownEnabled
    {
        get => (bool)GetValue(IsKeyDownEnabledProperty);
        set => SetValue(IsKeyDownEnabledProperty, value);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (!IsKeyDownEnabled && !IsFocusEngaged) return;
        base.OnKeyDown(e);
    }
}
