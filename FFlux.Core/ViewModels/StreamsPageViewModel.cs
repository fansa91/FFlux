using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Screenbox.Core.Enums;
using Screenbox.Core.Factories;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;

namespace Screenbox.Core.ViewModels;

/// <summary>
/// The result of a quick-add attempt, so the view can offer to expand a playlist.
/// </summary>
public sealed record QuickAddResult(
    StreamItem? Item,
    IReadOnlyList<StreamPlaylistEntry> PlaylistEntries,
    string? Error);

/// <summary>
/// Drives the network stream library page.
/// </summary>
/// <remarks>
/// The page owns the dialogs (rename / edit) and calls back into this view model, matching how
/// the playlist pages already work. Everything that touches the JSON file goes through
/// <see cref="IStreamManagerService"/> so a single debounced writer owns persistence.
/// </remarks>
public sealed partial class StreamsPageViewModel : ObservableRecipient
{
    private readonly IStreamManagerService _streamManager;
    private readonly IStreamUrlDetector _detector;
    private readonly MediaViewModelFactory _mediaFactory;

    /// <summary>Gets the filtered list shown by the page.</summary>
    [ObservableProperty] public partial IReadOnlyList<StreamItem> VisibleStreams { get; set; } = [];

    /// <summary>Gets whether the library has no entries at all, so the page can show its empty state.</summary>
    [ObservableProperty] public partial bool IsLibraryEmpty { get; set; }

    /// <summary>Gets whether entries exist but none match the current search.</summary>
    [ObservableProperty] public partial bool IsFilterEmpty { get; set; }

    [ObservableProperty] public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty] public partial string QuickAddUrl { get; set; } = string.Empty;

    [ObservableProperty] public partial bool IsBusy { get; set; }

    /// <summary>Gets the total number of saved entries, ignoring the search filter.</summary>
    public int TotalCount => _streamManager.Items.Count;

    public StreamsPageViewModel(
        IStreamManagerService streamManager,
        IStreamUrlDetector detector,
        MediaViewModelFactory mediaFactory)
    {
        _streamManager = streamManager;
        _detector = detector;
        _mediaFactory = mediaFactory;
    }

    /// <summary>
    /// Loads the JSON file once and refreshes the visible list.
    /// </summary>
    public async Task LoadAsync()
    {
        await _streamManager.LoadAsync();
        Refresh();
    }

    partial void OnSearchQueryChanged(string value) => Refresh();

    /// <summary>
    /// Classifies whatever is in the quick-add box, saves it and returns the outcome.
    /// </summary>
    public async Task<QuickAddResult> QuickAddAsync(CancellationToken cancellationToken = default)
    {
        string input = QuickAddUrl?.Trim() ?? string.Empty;
        return await AddUrlAsync(input, null, null, null, null, cancellationToken);
    }

    /// <summary>
    /// Adds a URL to the library, detecting its format and deriving a name when none is given.
    /// </summary>
    public async Task<QuickAddResult> AddUrlAsync(
        string url,
        string? name,
        string? userAgent,
        string? referer,
        string? headers,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new QuickAddResult(null, [], null);
        }

        if (!StreamUrlDetector.TryNormalize(url, out _, out string normalized))
        {
            return new QuickAddResult(null, [], "invalid-url");
        }

        IsBusy = true;
        try
        {
            StreamDetectionResult detection = await _detector.DetectAsync(
                normalized, userAgent, referer, cancellationToken);

            var item = new StreamItem
            {
                Url = normalized,
                Name = string.IsNullOrWhiteSpace(name) ? detection.SuggestedName : name.Trim(),
                FormatType = detection.FormatType,
                UserAgent = userAgent?.Trim() ?? string.Empty,
                Referer = referer?.Trim() ?? string.Empty,
                Headers = headers?.Trim() ?? string.Empty,
            };

            if (string.IsNullOrWhiteSpace(item.Name)) item.Name = item.Url;

            _streamManager.Add(item);
            QuickAddUrl = string.Empty;
            Refresh();

            // Extended M3U lists are worth offering to import, HLS manifests are not.
            IReadOnlyList<StreamPlaylistEntry> entries = detection.IsPlaylist
                ? await _detector.ReadPlaylistAsync(item.Url, item.UserAgent, item.Referer, cancellationToken)
                : [];

            return new QuickAddResult(item, entries, null);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Re-detects the format of an existing entry, used after the URL is edited by hand.
    /// </summary>
    public async Task RefreshFormatAsync(StreamItem item, CancellationToken cancellationToken = default)
    {
        if (item is null) return;

        StreamDetectionResult detection = await _detector.DetectAsync(
            item.Url, item.UserAgent, item.Referer, cancellationToken);
        if (detection.FormatType != StreamFormatType.Unknown)
        {
            item.FormatType = detection.FormatType;
        }

        if (string.IsNullOrWhiteSpace(item.Name))
        {
            item.Name = detection.SuggestedName;
        }
    }

    /// <summary>
    /// Sends a saved entry to the player. The player overlay takes over, which is the "switch to
    /// now playing" behaviour the page expects.
    /// </summary>
    public bool Play(StreamItem? item)
    {
        if (item is null) return false;

        if (!StreamUrlDetector.TryNormalize(item.Url, out Uri? uri, out string normalized) || uri is null)
        {
            Messenger.Send(new NotificationMessage(
                NotificationLevel.Error, NotificationKind.MediaLoadFailed, message: item.Url));
            return false;
        }

        // Use the normalized string so a URL saved before normalization (or edited by hand) still
        // reaches FFmpeg in a form it accepts.
        item.Url = normalized;

        MediaViewModel media = _mediaFactory.Create(
            uri, StreamOptionsHelper.ToFFmpegOptions(item), item.Name);
        Messenger.Send(new PlayMediaMessage(media));
        return true;
    }

    public void Rename(StreamItem? item, string newName)
    {
        if (item is null) return;
        _streamManager.Rename(item, newName);
        Refresh();
    }

    /// <summary>
    /// Applies an edited copy back onto the live entry.
    /// </summary>
    public void ApplyEdit(StreamItem? target, StreamItem? edited)
    {
        if (target is null || edited is null) return;
        _streamManager.Update(target, edited);
        Refresh();
    }

    public void Remove(StreamItem? item)
    {
        if (item is null) return;
        _streamManager.Remove(item);
        Refresh();
    }

    /// <summary>
    /// Imports every entry of a parsed M3U playlist, keeping the anti-hotlink settings of the
    /// playlist itself.
    /// </summary>
    public int ImportPlaylist(IReadOnlyList<StreamPlaylistEntry> entries, StreamItem? source)
    {
        if (entries is null || entries.Count == 0) return 0;

        foreach (StreamPlaylistEntry entry in entries)
        {
            var item = new StreamItem
            {
                Url = entry.Url,
                Name = string.IsNullOrWhiteSpace(entry.Name) ? _detector.SuggestName(entry.Url) : entry.Name,
                FormatType = _detector.Detect(entry.Url).FormatType,
                UserAgent = source?.UserAgent ?? string.Empty,
                Referer = source?.Referer ?? string.Empty,
                Headers = source?.Headers ?? string.Empty,
            };

            _streamManager.Add(item);
        }

        Refresh();
        return entries.Count;
    }

    /// <summary>
    /// Reads a playlist for the "expand" prompt. Returns an empty list when the URL is not an
    /// importable extended M3U list.
    /// </summary>
    public Task<IReadOnlyList<StreamPlaylistEntry>> ReadPlaylistAsync(
        StreamItem item,
        CancellationToken cancellationToken = default)
    {
        return item is null
            ? Task.FromResult<IReadOnlyList<StreamPlaylistEntry>>([])
            : _detector.ReadPlaylistAsync(item.Url, item.UserAgent, item.Referer, cancellationToken);
    }

    [RelayCommand]
    private async Task QuickAdd()
    {
        QuickAddResult result = await QuickAddAsync();
        if (result.Error == "invalid-url")
        {
            Messenger.Send(new NotificationMessage(
                NotificationLevel.Warning, NotificationKind.MediaLoadFailed, message: QuickAddUrl));
        }
    }

    [RelayCommand]
    private void PlayStream(StreamItem? item) => Play(item);

    /// <summary>
    /// Rebuilds <see cref="VisibleStreams"/> from the manager, applying the search filter.
    /// </summary>
    private void Refresh()
    {
        List<StreamItem> all = _streamManager.Items.ToList();
        IsLibraryEmpty = all.Count == 0;

        string query = SearchQuery?.Trim() ?? string.Empty;
        IEnumerable<StreamItem> filtered = query.Length == 0
            ? all
            : all.Where(i => Matches(i, query));

        List<StreamItem> visible = filtered.ToList();
        VisibleStreams = visible;
        IsFilterEmpty = !IsLibraryEmpty && visible.Count == 0;
        OnPropertyChanged(nameof(TotalCount));
    }

    private static bool Matches(StreamItem item, string query)
    {
        return Contains(item.Name, query)
               || Contains(item.Url, query)
               || Contains(item.BadgeText, query)
               || Contains(item.FormatType.ToString(), query);

        static bool Contains(string? value, string query) =>
            !string.IsNullOrEmpty(value) &&
            value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
