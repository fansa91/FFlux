using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.MediaSources;
using Screenbox.Core.Models;

namespace Screenbox.Core.ViewModels;

/// <summary>Holds the lyrics of the current track and the index of the active line.</summary>
public sealed partial class LyricsViewModel : ObservableObject
{
    public ObservableCollection<LyricLine> Lines { get; } = new();

    [ObservableProperty] public partial int CurrentLineIndex { get; set; } = -1;

    [ObservableProperty] public partial bool HasLyrics { get; set; }

    private readonly Dictionary<string, IReadOnlyList<LyricLine>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public LyricsViewModel()
    {
    }

    public async Task LoadForCurrentItemAsync(MediaViewModel? media)
    {
        Reset();
        if (media is null || media.MediaType != MediaPlaybackType.Music) return;

        string path = media.Location;
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            IReadOnlyList<LyricLine> lines;
            if (_cache.TryGetValue(path, out IReadOnlyList<LyricLine>? cached))
            {
                lines = cached;
            }
            else
            {
                lines = await LoadFromFileAsync(media);
                if (lines.Count > 0) _cache[path] = lines;
            }

            foreach (LyricLine line in lines) Lines.Add(line);
            HasLyrics = Lines.Count > 0;
        }
        catch (Exception)
        {
            // Lyrics are optional; never let a malformed file affect playback.
            HasLyrics = false;
        }
    }

    private async Task<IReadOnlyList<LyricLine>> LoadFromFileAsync(MediaViewModel media)
    {
        if (media.Source is not IFileMediaSource fileSource) return Array.Empty<LyricLine>();
        Windows.Storage.StorageFile? file = await fileSource.GetStorageFileAsync();
        if (file is null) return Array.Empty<LyricLine>();

        Windows.Storage.StorageFile? lyricFile = await LyricsLoader.FindAsync(file);
        if (lyricFile is null) return Array.Empty<LyricLine>();

        string text = await Windows.Storage.FileIO.ReadTextAsync(lyricFile);
        return LrcParser.Parse(text);
    }

    public void UpdatePosition(TimeSpan position)
    {
        if (!HasLyrics) return;
        int index = LrcParser.FindLineIndex(Lines, position);
        if (index != CurrentLineIndex) CurrentLineIndex = index;
    }

    public void Reset()
    {
        Lines.Clear();
        CurrentLineIndex = -1;
        HasLyrics = false;
    }
}
