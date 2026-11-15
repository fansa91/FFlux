using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Screenbox.Core.Models;
using Screenbox.Core.ViewModels;
using Screenbox.Helpers;

namespace Screenbox.Controls;

/// <summary>
/// Builds the property list shown by the properties dialog.
///
/// This is deliberately a plain class that produces a visual tree instead of a UserControl
/// subclass, and the tree is assembled in code instead of XAML. Both restrictions come from the
/// WinUI 3 migration:
///
/// * A hand-written managed subclass of a WinRT control cannot expose the layout override
///   interfaces to the XAML host, so laying it out fails with InvalidCastException hr=0x80004002
///   (E_NOINTERFACE).
/// * Its XAML form never loaded either: Application.LoadComponent failed with
///   XamlParseException hr=0x802B000A ("XAML parsing failed") even though identical markup parses
///   and lays out correctly through XamlReader.
///
/// Building the tree directly avoids both, and the list is only assembled when the dialog opens,
/// so there is nothing to gain from a template.
/// </summary>
internal sealed class PropertyPanel
{
    private readonly PropertyViewModel _viewModel;
    private readonly StackPanel _sections;

    public PropertyPanel()
    {
        _viewModel = Ioc.Default.GetRequiredService<PropertyViewModel>();

        _sections = new StackPanel { Orientation = Orientation.Vertical };
        Root = new ScrollViewer
        {
            Content = _sections,
            HorizontalScrollMode = ScrollMode.Disabled,
            ZoomMode = ZoomMode.Disabled,
        };
    }

    /// <summary>
    /// Gets the element to host in the dialog.
    /// </summary>
    public FrameworkElement Root { get; }

    /// <summary>
    /// Fills the panel from the shell's file properties only. Renders immediately, which keeps the
    /// dialog from flashing an empty list while the media is probed.
    /// </summary>
    public void Update(MediaViewModel? media)
    {
        if (media == null) return;
        _viewModel.UpdateProperties(media);
        Render(media);
    }

    /// <summary>
    /// Fills the panel, then re-renders once the codec details have been read from the file.
    /// </summary>
    public async Task UpdateAsync(MediaViewModel? media)
    {
        if (media == null) return;
        await _viewModel.LoadPropertiesAsync(media);
        Render(media);
    }

    private void Render(MediaViewModel media)
    {
        _sections.Children.Clear();

        Append(Strings.Resources.Media, _viewModel.MediaProperties);
        Append(Strings.Resources.Video, _viewModel.VideoProperties);
        Append(Strings.Resources.Music, _viewModel.AudioProperties);
        Append(Strings.Resources.Tracks, _viewModel.TrackProperties);
        AppendFileSection(media);
    }

    private void AppendFileSection(MediaViewModel media)
    {
        StackPanel section = BuildSection(Strings.Resources.File, _viewModel.FileProperties);
        _sections.Children.Add(section);

        section.Children.Add(new TextBlock
        {
            Text = Strings.Resources.PropertyFileLocation,
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 12,
            Opacity = 0.65,
        });

        section.Children.Add(new TextBlock
        {
            Text = media.Location,
            Margin = new Thickness(0, 0, 0, 8),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        });

        section.Children.Add(new HyperlinkButton
        {
            Content = Strings.Resources.OpenFileLocation,
            Command = _viewModel.OpenFileLocationCommand,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        });
    }

    /// <summary>
    /// Appends one captioned group of key/value rows. Empty groups are dropped rather than added, so
    /// the panel never shows a header with nothing underneath it.
    /// </summary>
    private void Append(string header, IReadOnlyList<MediaMetadata> rows)
    {
        if (rows.Count == 0) return;
        _sections.Children.Add(BuildSection(header, rows));
    }

    private static StackPanel BuildSection(string header, IReadOnlyList<MediaMetadata> rows)
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        section.Children.Add(new TextBlock
        {
            Text = header,
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });

        foreach (MediaMetadata row in rows)
        {
            section.Children.Add(BuildRow(row));
        }

        return section;
    }

    private static Grid BuildRow(MediaMetadata row)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.6, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = ItemLabelHelper.GetMediaPropertyResourceString(row.Key),
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 12,
            Opacity = 0.65,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        Grid.SetColumn(label, 0);

        var value = new TextBlock
        {
            Text = row.Value,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(value, 1);

        grid.Children.Add(label);
        grid.Children.Add(value);
        return grid;
    }
}
