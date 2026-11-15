using Screenbox.Core.Common;
using Screenbox.Core.Helpers;
using Screenbox.Core.ViewModels;
using Screenbox.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Screenbox.Controls;

// The Content Dialog item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Screenbox.Dialogs;

public sealed partial class PropertiesDialog : ContentDialog, IPropertiesDialog
{
    public static readonly DependencyProperty MediaProperty = DependencyProperty.Register(
        nameof(Media),
        typeof(MediaViewModel),
        typeof(PropertiesDialog),
        new PropertyMetadata(null, OnMediaChanged));

    private readonly PropertyPanel _panel;

    public MediaViewModel? Media
    {
        get => (MediaViewModel?)GetValue(MediaProperty);
        set => SetValue(MediaProperty, value);
    }

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    public PropertiesDialog()
    {
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)WindowAccessor.Current.Content).RequestedTheme;

        _panel = new PropertyPanel();
        PropertiesHost.Children.Add(_panel.Root);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _panel.UpdateAsync(Media);
    }

    private static void OnMediaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PropertiesDialog)d)._panel.Update(e.NewValue as MediaViewModel);
    }
}
