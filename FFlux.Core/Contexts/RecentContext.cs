using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Screenbox.Core.ViewModels;

namespace Screenbox.Core.Contexts;

/// <summary>
/// Context for holding the application-wide recent media items.
/// </summary>
public sealed partial class RecentContext : ObservableObject
{
    /// <summary>
    /// Gets the collection of recent media items.
    /// </summary>
    public ObservableCollection<MediaViewModel> Recent { get; } = new();

    /// <summary>
    /// Gets the cache from media location to the view model currently displayed for it, so a
    /// refresh can reuse instances that already have their details and thumbnail loaded.
    /// </summary>
    public Dictionary<string, MediaViewModel> PathToMediaMappings { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets a value indicating whether the recent media list has been loaded from storage.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoaded { get; set; }
}
