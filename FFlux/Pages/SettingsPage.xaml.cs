using System;
using System.Linq;
using CommunityToolkit.Mvvm.DependencyInjection;
using Screenbox.Core.ViewModels;
using Microsoft.UI.Xaml;
using Windows.Globalization;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Navigation;
using Screenbox.Core.Helpers;
using Screenbox.Helpers;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Screenbox.Pages;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private const string SystemDefaultLanguage = "LanguageSystemDefault";

    internal SettingsPageViewModel ViewModel => (SettingsPageViewModel)DataContext;

    internal CommonViewModel Common { get; }

    // The view model uses an empty string for "system default". Using that value directly in the
    // ComboBox would result in a blank selected item on first launch.
    private string SelectedLanguage
    {
        get => string.IsNullOrEmpty(ViewModel.Language) ? SystemDefaultLanguage : ViewModel.Language;
        set => ViewModel.Language = value.Equals(SystemDefaultLanguage) ? string.Empty : value;
    }

    private string[] PlaybackOptionsHelpTextParts { get; }

    private string[] LanguageOptions => _languageOptions;

    private static readonly string[] _languageOptions = [SystemDefaultLanguage, .. AppLanguageOverride.ShippedLanguages];

    /// <summary>
    /// The "Version x.y.z" caption of the About section.
    /// </summary>
    /// <remarks>
    /// The ReswPlus <c>VersionText</c> macro reads <c>Package.Current</c>, which throws
    /// <c>0x80073D54</c> in an unpackaged process - and the binding evaluated on page load, so
    /// opening the settings page crashed the app. The format string is read from the resource file
    /// directly and filled with the assembly version instead.
    /// </remarks>
    private string VersionLabel { get; }

    public SettingsPage()
    {
        this.InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<SettingsPageViewModel>();
        Common = Ioc.Default.GetRequiredService<CommonViewModel>();

        VersionLabel = FormatVersionLabel();

        var helpText = Strings.Resources.VlcCommandLineHelpText;
        PlaybackOptionsHelpTextParts = helpText.Contains("{0}")
            ? helpText.Split("{0}").Select(s => s.Trim()).Take(2).ToArray()
            : new[] { helpText, string.Empty };
    }

    private static string FormatVersionLabel()
    {
        string version = AppVersion.Product;
        if (string.IsNullOrEmpty(version))
        {
            return string.Empty;
        }

        string format = AppResourceLoader.GetString("VersionText");
        if (string.IsNullOrEmpty(format))
        {
            return version;
        }

        try
        {
            return string.Format(format, version);
        }
        catch (FormatException)
        {
            return version;
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadLibraryLocations();
        await ViewModel.LoadFrameCaptureFolderAsync();
        await AudioVisualSelector.ViewModel.InitializeVisualizers();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.OnNavigatedFrom();
    }

    private async void ManageSystemIndexingCard_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:search"));
    }

    [DynamicWindowsRuntimeCast(typeof(Frame))]
    private void ThemeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WindowAccessor.Current.Content is Frame rootFrame)
        {
            rootFrame.RequestedTheme = ViewModel.GeneralSettings.Theme;
        }
    }

    private void LanguageComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AppLanguageOverride.Current = ViewModel.Language;
    }

    private void ChangeFrameCaptureLocationHyperlink_OnClick(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        ViewModel.ChangeFrameCaptureLocationCommand.Execute(null);
    }
}
